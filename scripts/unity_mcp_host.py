#!/usr/bin/env python3
"""Bridge the official Unity CLI MCP server to HTTP with catalog notifications."""
from __future__ import annotations

import argparse
from contextlib import contextmanager, suppress
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass, field
import hashlib
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import queue
from pathlib import Path
import re
import signal
import socket
import subprocess
import sys
import threading
import time
from types import FrameType
from typing import Any, Dict, Generator, Optional, Sequence, cast
from urllib.error import URLError
from urllib.parse import urlsplit
from urllib.request import ProxyHandler, build_opener
import uuid

sys.dont_write_bytecode = True
from unity_mcp_settings import BridgeSettings, resolve_settings

JsonMap = Dict[str, Any]
ROOT = Path(__file__).resolve().parent.parent


def log(message: str, error: bool = False) -> None:
    stream = sys.stderr.buffer if error else sys.stdout.buffer
    stream.write((message + '\n').encode('utf-8', errors='replace'))
    stream.flush()


def rpc_error(identifier: object, code: int, message: str) -> JsonMap:
    return {'jsonrpc': '2.0', 'id': identifier, 'error': {'code': code, 'message': message}}


@dataclass
class Pending:
    identifier: object
    ready: threading.Event = field(default_factory=threading.Event)
    response: Optional[JsonMap] = None


class Bridge:
    def __init__(self, settings: BridgeSettings) -> None:
        self.settings = settings
        self.instance = str(uuid.uuid4())
        self.source_revision = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
        self.child: Optional[subprocess.Popen[bytes]] = None
        self.input_queue: Optional[queue.Queue[bytes]] = None
        self.lock = threading.RLock()
        self.pending: Dict[int, Pending] = {}
        self.next_id = 0
        self.last_spawn = float('-inf')
        self.closed = False
        self.session_key = os.urandom(32)
        self.streams: Dict[str, queue.Queue[Optional[JsonMap]]] = {}

    def new_session(self) -> str:
        nonce = uuid.uuid4().hex
        with self.lock:
            return nonce + '.' + hmac.digest(self.session_key, nonce.encode('ascii'), 'sha256').hex()

    def valid_session(self, session: str) -> bool:
        nonce, _, signature = session.partition('.')
        with self.lock:
            expected = hmac.digest(self.session_key, nonce.encode('utf-8'), 'sha256').hex()
            return bool(nonce) and hmac.compare_digest(signature.encode('utf-8'), expected.encode('ascii'))

    def publish_catalog_change(self) -> None:
        with self.lock:
            for messages in self.streams.values():
                if messages.empty():
                    messages.put_nowait({'jsonrpc': '2.0', 'method': 'notifications/tools/list_changed'})

    def end_streams(self) -> None:
        for messages in self.streams.values():
            with suppress(queue.Empty):
                messages.get_nowait()
            messages.put_nowait(None)
        self.streams.clear()
        self.session_key = os.urandom(32)

    @contextmanager
    def stream(self, session: str) -> Generator[queue.Queue[Optional[JsonMap]], None, None]:
        messages: queue.Queue[Optional[JsonMap]] = queue.Queue(maxsize=1)
        with self.lock:
            if session not in self.streams and len(self.streams) >= 128:
                raise ValueError('Too many notification streams.')
            previous = self.streams.get(session)
            if previous is not None:
                with suppress(queue.Empty):
                    previous.get_nowait()
                previous.put_nowait(None)
            self.streams[session] = messages
            # Refresh after any catalog change that preceded this GET or a reconnect.
            messages.put_nowait({'jsonrpc': '2.0', 'method': 'notifications/tools/list_changed'})
        try:
            yield messages
        finally:
            with self.lock:
                if self.streams.get(session) is messages:
                    del self.streams[session]

    def fail_pending(self, message: str) -> None:
        for pending in self.pending.values():
            pending.response = rpc_error(pending.identifier, -32000, message)
            pending.ready.set()
        self.pending.clear()

    def retire(self, child: subprocess.Popen[bytes], reason: str) -> None:
        with self.lock:
            if self.child is not child:
                return
            self.child = None
            self.input_queue = None
            self.fail_pending(reason)
            self.end_streams()
        with suppress(OSError):
            child.kill()
        # Only the writer owns stdin; closing it here can wait on its IO lock.

    def read_stdout(self, child: subprocess.Popen[bytes]) -> None:
        assert child.stdout is not None
        for line in child.stdout:
            if not line.strip():
                continue
            try:
                parsed: object = json.loads(line.decode('utf-8', errors='replace'))
                if not isinstance(parsed, dict):
                    raise ValueError('Expected a JSON object')
                message = cast(JsonMap, parsed)
            except ValueError:
                log('[unity-mcp] non-JSON stdout: ' + line.decode('utf-8', errors='replace').strip(), True)
                continue
            identifier = message.get('id')
            if identifier is None:
                if message.get('method') == 'notifications/tools/list_changed':
                    with self.lock:
                        if self.child is child:
                            self.publish_catalog_change()
                continue
            with self.lock:
                if self.child is not child:
                    continue
                pending = self.pending.pop(identifier, None) if isinstance(identifier, int) and not message.get('method') else None
                if pending is not None:
                    pending.response = {**message, 'id': pending.identifier}
                    pending.ready.set()
                elif message.get('method'):
                    try:
                        self.write(rpc_error(identifier, -32601, 'Method not supported by bridge: ' + str(message['method'])))
                    except (OSError, ValueError):
                        pass
        self.retire(child, 'Unity CLI exited; retry the request.')
        child.wait()
        child.stdout.close()

    def read_stderr(self, child: subprocess.Popen[bytes]) -> None:
        assert child.stderr is not None
        for line in child.stderr:
            log('[unity mcp] ' + line.decode('utf-8', errors='replace').rstrip(), True)
        child.stderr.close()

    def write_stdin(self, child: subprocess.Popen[bytes], messages: queue.Queue[bytes]) -> None:
        assert child.stdin is not None
        try:
            while child.poll() is None:
                try:
                    message = messages.get(timeout=0.5)
                except queue.Empty:
                    continue
                child.stdin.write(message)
                child.stdin.flush()
        except OSError:
            self.retire(child, 'Unity CLI input closed; retry the request.')
        finally:
            with suppress(OSError):
                child.stdin.close()

    def ensure_child(self) -> subprocess.Popen[bytes]:
        if self.closed:
            raise ValueError('Bridge stopped.')
        if self.child is not None and self.child.poll() is None:
            return self.child
        if time.monotonic() - self.last_spawn < 1:
            raise ValueError('Unity CLI is not startable; install the Unity CLI or set UNITY_CLI to its path.')
        self.last_spawn = time.monotonic()
        self.fail_pending('Unity CLI restarted; retry the request.')
        self.child = subprocess.Popen([self.settings['cliCommand'], *self.settings['cliArgs']],
                                      stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        child = self.child
        messages: queue.Queue[bytes] = queue.Queue(maxsize=128)
        self.input_queue = messages
        threading.Thread(target=self.write_stdin, args=(child, messages), daemon=True).start()
        threading.Thread(target=self.read_stdout, args=(child,), daemon=True).start()
        threading.Thread(target=self.read_stderr, args=(child,), daemon=True).start()
        return child

    def write(self, message: JsonMap) -> None:
        self.ensure_child()
        assert self.input_queue is not None
        try:
            self.input_queue.put_nowait((json.dumps(message, ensure_ascii=False) + '\n').encode('utf-8'))
        except queue.Full as error:
            raise ValueError('Unity CLI input queue is full; retry the request.') from error

    def request(self, message: JsonMap) -> JsonMap:
        with self.lock:
            identifier: Optional[int] = None
            try:
                self.ensure_child()
                self.next_id += 1
                identifier = self.next_id
                pending = Pending(message['id'])
                self.pending[identifier] = pending
                self.write({**message, 'id': identifier})
            except (OSError, ValueError) as error:
                if identifier is not None:
                    self.pending.pop(identifier, None)
                return rpc_error(message['id'], -32000, str(error))
        timeout = float(self.settings['timeout']) / 1000
        if not pending.ready.wait(timeout):
            with self.lock:
                self.pending.pop(identifier, None)
            return rpc_error(message['id'], -32000, f"Timed out after {self.settings['timeout']}ms waiting for Unity CLI.")
        return pending.response or rpc_error(message['id'], -32000, 'Unity CLI returned no response.')

    def diagnostics(self) -> JsonMap:
        arguments = [self.settings['cliCommand'], 'status', '--format', 'json']
        if self.settings['projectPath']:
            arguments.extend(('--project-path', self.settings['projectPath']))
        report: JsonMap = {'health': self.health()}
        try:
            result = subprocess.run(arguments, capture_output=True, text=True, encoding='utf-8', timeout=15, check=False)
            report['statusExitCode'] = result.returncode
            parsed: object = json.loads(result.stdout)
            status = cast(JsonMap, parsed) if isinstance(parsed, dict) else {}
            reasons = status.get('errors', [])
            entries = cast(list[object], reasons) if isinstance(reasons, list) else []
            report['discoveryErrors'] = [
                {key: cast(JsonMap, reason)[key] for key in ('code', 'message') if key in cast(JsonMap, reason)}
                for reason in entries if isinstance(reason, dict)
            ]
            report['statusSuccess'] = status.get('success')
        except (OSError, ValueError, subprocess.TimeoutExpired) as error:
            report['probeError'] = type(error).__name__ + ': pinned Unity status did not return structured diagnostics.'
        return report

    def handle(self, message: object) -> Optional[object]:
        if isinstance(message, list):
            entries = cast(list[object], message)
            if not entries:
                return None
            with ThreadPoolExecutor(max_workers=min(len(entries), 32)) as executor:
                responses = [result for result in executor.map(self.handle, entries) if result is not None]
            return responses[0] if len(responses) == 1 else responses or None
        if not isinstance(message, dict):
            return rpc_error(None, -32600, 'Invalid JSON-RPC message.')
        request = cast(JsonMap, message)
        if request.get('id') is None:
            try:
                with self.lock:
                    self.write(request)
            except (OSError, ValueError) as error:
                log('[unity-mcp] dropped notification: ' + str(error), True)
            return None
        params = request.get('params', {})
        if request.get('method') == 'tools/call' and isinstance(params, dict) and cast(JsonMap, params).get('name') == 'bridge_diagnostics':
            text = json.dumps(self.diagnostics(), ensure_ascii=False)
            if self.settings['token']:
                text = text.replace(self.settings['token'], '<redacted>')
            return {'jsonrpc': '2.0', 'id': request['id'], 'result': {'content': [{'type': 'text', 'text': text}]}}
        response = self.request(request)
        if request.get('method') == 'tools/list' and isinstance(response.get('result'), dict):
            result = cast(JsonMap, response['result'])
            tools = result.get('tools')
            if isinstance(tools, list):
                cast(list[JsonMap], tools).append({'name': 'bridge_diagnostics',
                              'description': 'Read pinned host CLI discovery errors even when no Editor tools are available. Never changes the Editor.',
                              'inputSchema': {'type': 'object', 'properties': {}, 'additionalProperties': False}})
        return response

    def health(self) -> JsonMap:
        with self.lock:
            child = self.child
            return {'bridge': 'dataviz-unity-mcp', 'instanceId': self.instance, 'bridgePid': os.getpid(),
                    'ok': child is not None and child.poll() is None,
                    'cli': ' '.join((self.settings['cliCommand'], *self.settings['cliArgs'])),
                    'projectPath': self.settings['projectPath'], 'authRequired': bool(self.settings['token']),
                    'pid': child.pid if child is not None else None,
                    'sourceRevision': self.source_revision, 'pythonVersion': sys.version.split()[0],
                    'protocolVersion': 2, 'pendingRequests': len(self.pending),
                    'cliRunning': child is not None and child.poll() is None, 'editorReady': None}

    def reconnect(self) -> bool:
        with self.lock:
            if self.pending or self.closed:
                return False
            child = self.child
            if child is not None:
                self.retire(child, 'Unity CLI reconnect requested; initialize again.')
            else:
                self.end_streams()
            self.last_spawn = float('-inf')
            return True

    def close(self) -> None:
        with self.lock:
            self.closed = True
            child = self.child
            self.child = None
            self.input_queue = None
            self.fail_pending('Bridge stopped.')
            self.end_streams()
        if child is not None:
            try:
                child.kill()
                child.wait(timeout=2)
            except (OSError, subprocess.TimeoutExpired):
                pass


class BridgeServer(ThreadingHTTPServer):
    allow_reuse_address = sys.platform != 'win32'
    daemon_threads = True
    bridge: Bridge

    def __init__(self, host: str, port: int, bridge: Bridge) -> None:
        self.bridge = bridge
        self.stop_requested = threading.Event()
        self.address_family = socket.AF_INET6 if ':' in host else socket.AF_INET
        super().__init__((host, port), Handler)


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    timeout = 180

    def body(self) -> bytes:
        if self.headers.get('Transfer-Encoding', '').lower() != 'chunked':
            return self.rfile.read(int(self.headers.get('Content-Length') or 0))
        chunks: list[bytes] = []
        while True:
            size = int(self.rfile.readline().split(b';', 1)[0].strip(), 16)
            if size == 0:
                while self.rfile.readline().strip():
                    pass
                return b''.join(chunks)
            chunks.append(self.rfile.read(size))
            if self.rfile.read(2) != b'\r\n':
                raise ValueError('Invalid chunk terminator')

    def respond(self, status: int, payload: Optional[object] = None, early: bool = False, session: str = '') -> None:
        data = b'' if payload is None else json.dumps(payload, ensure_ascii=False).encode('utf-8')
        self.send_response(status)
        if session:
            self.send_header('Mcp-Session-Id', session)
        if status != 204:
            self.send_header('Content-Length', str(len(data)))
        if payload is not None:
            self.send_header('Content-Type', 'application/json')
        if status == 405:
            self.send_header('Allow', 'GET, POST, DELETE')
        self.end_headers()
        if self.command != 'HEAD':
            self.wfile.write(data)
        self.wfile.flush()
        if early:
            self.body()

    def events(self, bridge: Bridge, session: str) -> None:
        with bridge.stream(session) as messages:
            self.send_response(200)
            self.send_header('Content-Type', 'text/event-stream')
            self.send_header('Cache-Control', 'no-cache')
            self.send_header('Connection', 'close')
            self.end_headers()
            self.close_connection = True
            self.wfile.write(b': connected\n\n')
            self.wfile.flush()
            while not bridge.closed:
                try:
                    message = messages.get(timeout=1)
                except queue.Empty:
                    self.wfile.write(b': heartbeat\n\n')
                else:
                    if message is None:
                        return
                    self.wfile.write(('event: message\ndata: ' + json.dumps(message) + '\n\n').encode('utf-8'))
                self.wfile.flush()

    def handle_rpc(self) -> None:
        bridge = cast(BridgeServer, self.server).bridge
        route = urlsplit(self.path).path
        try:
            if route == '/healthz':
                self.respond(200, bridge.health(), early=True)
            elif route not in ('/mcp', '/control'):
                self.respond(404, early=True)
            elif bridge.settings['token'] and not hmac.compare_digest(
                self.headers.get('Authorization', '').encode('utf-8'), ('Bearer ' + bridge.settings['token']).encode('utf-8')
            ):
                self.respond(401, {'error': 'unauthorized'}, early=True)
            elif route == '/control':
                if self.command != 'POST':
                    self.respond(405, early=True)
                    return
                try:
                    control: object = json.loads(self.body())
                except ValueError as error:
                    self.respond(400, {'error': 'Invalid control JSON: ' + str(error)})
                    return
                values = cast(JsonMap, control) if isinstance(control, dict) else {}
                if values.get('instanceId') != bridge.instance:
                    self.respond(409, {'error': 'Bridge instance changed; read health again.'})
                elif values.get('action') == 'reconnect':
                    if bridge.reconnect():
                        self.respond(200, {'retired': True, 'message': 'Initialize again to discover the Editor.'})
                    else:
                        self.respond(409, {'error': 'Requests are in flight; reconnect refused without replaying them.'})
                elif values.get('action') == 'stop':
                    self.respond(200, {'stopping': True})
                    cast(BridgeServer, self.server).stop_requested.set()
                else:
                    self.respond(400, {'error': 'Unknown control action.'})
            elif self.headers.get('Mcp-Session-Id') and not bridge.valid_session(self.headers['Mcp-Session-Id']):
                self.respond(404, {'error': 'Session expired; initialize again.'}, early=True)
            elif self.command == 'GET' and self.headers.get('Mcp-Session-Id'):
                if 'text/event-stream' not in self.headers.get('Accept', ''):
                    self.respond(406, early=True)
                else:
                    try:
                        self.events(bridge, self.headers['Mcp-Session-Id'])
                    except ValueError as error:
                        self.respond(503, {'error': str(error)})
            elif self.command == 'DELETE':
                with bridge.lock:
                    messages = bridge.streams.pop(self.headers.get('Mcp-Session-Id', ''), None)
                    if messages is not None:
                        with suppress(queue.Empty):
                            messages.get_nowait()
                        messages.put_nowait(None)
                self.respond(204, early=True)
            elif self.command != 'POST':
                self.respond(405, early=True)
            else:
                try:
                    message: object = json.loads(self.body().decode('utf-8', errors='replace'))
                except ValueError as error:
                    self.respond(400, rpc_error(None, -32700, 'Parse error: ' + str(error)))
                    return
                result = bridge.handle(message)
                request = cast(JsonMap, message) if isinstance(message, dict) else {}
                initialized = request.get('method') == 'initialize' and isinstance(result, dict) and 'result' in result
                self.respond(202 if result is None else 200, cast(object, result), session=bridge.new_session() if initialized else '')
        except (OSError, ValueError):
            # A disconnected HTTP client does not own the shared CLI lifetime.
            pass

    do_GET = do_POST = do_DELETE = do_PUT = do_PATCH = do_HEAD = do_OPTIONS = do_TRACE = handle_rpc

    def log_message(self, format: str, *args: object) -> None:
        pass


class DockerHostRouteError(ValueError):
    pass


def read_health(port: int, hostname: str) -> JsonMap:
    host = '[' + hostname + ']' if ':' in hostname else hostname
    with build_opener(ProxyHandler({})).open(f'http://{host}:{port}/healthz', timeout=3) as response:
        value: object = json.load(response)
    if not isinstance(value, dict):
        raise ValueError('Health returned no bridge object')
    return cast(JsonMap, value)


def verify_docker_host_route(port: int, host: str, instance: str) -> None:
    if sys.platform != 'win32' or host not in ('0.0.0.0', '::'):
        return
    try:
        if not instance or read_health(port, 'host.docker.internal').get('instanceId') != instance:
            raise ValueError('the Docker host endpoint answered as a different listener')
        log(f'[unity-mcp] Docker host route verified on port {port} (instance {instance}).')
    except (OSError, ValueError) as error:
        if isinstance(error, URLError) and isinstance(error.reason, socket.gaierror) and error.reason.errno == socket.EAI_NONAME:
            log('[unity-mcp] Docker host name is unavailable; only the local route was verified.')
            return
        raise DockerHostRouteError(f'Docker host route failed on host.docker.internal:{port}: {error}. '
                                  "Remove this port from VS Code's Ports view; trying another port.") from error


def port_free(port: int, host: str) -> bool:
    for address in (host, '127.0.0.1') if host in ('0.0.0.0', '::', 'localhost') else (host,):
        try:
            with socket.socket(socket.AF_INET6 if ':' in address else socket.AF_INET) as probe:
                # Windows reuse can claim an active port; default binding also permits TIME_WAIT reuse.
                if sys.platform != 'win32':
                    probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                probe.bind((address, port))
        except OSError:
            return False
    return True


def same_project(requested: str, actual: object) -> bool:
    if not isinstance(actual, str):
        return False
    if not requested or not actual:
        return requested == actual
    if not os.path.isabs(actual):
        return False
    try:
        return os.path.samefile(requested, actual)
    except (OSError, ValueError):
        return False


def existing_bridge(port: int, host: str, project: str) -> bool:
    try:
        health = read_health(port, '127.0.0.1')
        if health.get('bridge') != 'dataviz-unity-mcp' and not re.search(r'(?:^|[/\\])unity(?:\.exe)? mcp(?: |$)', str(health.get('cli', ''))):
            return False
        verify_docker_host_route(port, host, health.get('instanceId', ''))
    except (OSError, ValueError):
        return False
    actual = health.get('projectPath')
    if not same_project(project, actual):
        running = (actual or '<unpinned>') if isinstance(actual, str) else '<unknown>'
        raise ValueError(f"Bridge project mismatch on port {port}: requested {project or '<unpinned>'!r}, "
                         f"running {running!r}. "
                         'The existing bridge is unchanged. Use a different --port or stop that bridge before restarting.')
    return True


def persist_port(workspace: Path, port: int) -> bool:
    path = workspace / '.env.local'
    try:
        content = path.read_bytes().decode('utf-8')
    except OSError:
        content = '# Machine-local credentials (gitignored).\n'
    line = f'UNITY_MCP_HTTP_PORT={port}'
    existing = re.search(r'^UNITY_MCP_HTTP_PORT=.*$', content, re.MULTILINE)
    if existing:
        if existing.group().strip() == line:
            return False
        content = content[:existing.start()] + line + content[existing.end():]
    elif port != 9020:
        content = content.rstrip('\n') + '\n\n# Auto-persisted Unity MCP bridge port\n' + line + '\n'
    else:
        return False
    try:
        path.write_bytes(content.encode('utf-8'))
        return True
    except OSError as error:
        log('[unity-mcp] Could not persist port to .env.local: ' + str(error), True)
        return False


def serve(workspace: Path, settings: BridgeSettings, preferred: int, host: str, done: threading.Event) -> int:
    if existing_bridge(preferred, host, settings['projectPath']):
        log(f'[unity-mcp] An HTTP unity-mcp bridge is already running on port {preferred}; run unity:mcp:doctor to check Editor readiness.')
        return 0
    for port in range(preferred, min(preferred + 100, 65535) + 1):
        if not port_free(port, host):
            log(f'[unity-mcp] Port {port} is busy; trying another port.')
            continue
        bridge = Bridge(settings)
        server = BridgeServer(host, port, bridge)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            hostname = '127.0.0.1' if host == '0.0.0.0' else '::1' if host == '::' else host
            try:
                if read_health(port, hostname).get('instanceId') != bridge.instance:
                    raise ValueError('the loopback endpoint did not answer as this bridge instance')
            except (OSError, ValueError) as error:
                raise ValueError('Local HTTP self-check failed: ' + str(error)) from error
            log(f'[unity-mcp] Local HTTP self-check passed (bridge PID {os.getpid()}, instance {bridge.instance}).')
            verify_docker_host_route(port, host, bridge.instance)
            log(f"[unity-mcp] Official Unity CLI MCP bridge: {bridge.health()['cli']}")
            log(f'[unity-mcp] HTTP MCP server listening on http://{host}:{port}/mcp')
            log(f'[unity-mcp] Health: http://{host}:{port}/healthz')
            log('[unity-mcp] Auth: ' + ('bearer token required' if settings['token'] else 'OPEN (no UNITY_MCP_TOKEN configured)'))
            if persist_port(workspace, port):
                log(f'[unity-mcp] Persisted UNITY_MCP_HTTP_PORT={port} to .env.local so client configs follow.')
                if settings['autoSync'] == '0':
                    log('[unity-mcp] AUTO_SYNC disabled; run `npm run mcp:sync` to update MCP clients.')
                else:
                    log('[unity-mcp] Syncing MCP client configs across agentic frontends…')
                    try:
                        result = subprocess.run([sys.executable, str(workspace / 'scripts' / 'mcp_configure.py')], cwd=workspace,
                                                env={**os.environ, 'UNITY_MCP_HTTP_PORT': str(port)}, timeout=30, check=False)
                        if result.returncode:
                            raise ValueError('configurator failed')
                        log('[unity-mcp] MCP client configs updated (unity URL follows the new port).')
                    except (OSError, ValueError, subprocess.TimeoutExpired):
                        log('[unity-mcp] Config sync failed; run `npm run mcp:sync` manually.')
            log('[unity-mcp] Startup complete.')
            while not done.is_set() and not server.stop_requested.is_set():
                # Bounded sleep lets Python dispatch Windows console signals.
                time.sleep(0.1)
            return 0
        except DockerHostRouteError as error:
            log(str(error), True)
        finally:
            bridge.close()
            server.shutdown()
            server.server_close()
    raise ValueError(f'No verified bridge port is available in {preferred}-{min(preferred + 100, 65535)}. Remove VS Code reverse tunnels or check Docker Desktop networking.')


def control_bridge(settings: BridgeSettings, port: int, action: str) -> int:
    from urllib.request import Request
    health = read_health(port, '127.0.0.1')
    if health.get('bridge') != 'dataviz-unity-mcp' or not same_project(settings['projectPath'], health.get('projectPath')):
        raise ValueError('Control refused: listener does not own the requested project.')
    if health.get('protocolVersion') != 2:
        raise ValueError(f"This is an older bridge (PID {health.get('bridgePid')}); it cannot accept control requests. "
                         'Stop that verified bridge process once, then start the updated bridge. Do not stop Unity.')
    data = json.dumps({'instanceId': health['instanceId'], 'action': action}).encode('utf-8')
    headers = {'Content-Type': 'application/json'}
    request = Request(f'http://127.0.0.1:{port}/control', data=data, headers=headers)
    if settings['token']:
        request.add_unredirected_header('Authorization', 'Bearer ' + settings['token'])
    try:
        with build_opener(ProxyHandler({})).open(request, timeout=5) as response:
            result: object = json.load(response)
    except URLError as error:
        raise ValueError(f'Bridge control failed: {error}. In-flight requests are never replayed.') from error
    log('[unity-mcp] ' + json.dumps(result))
    return 0


def main(arguments: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--host', default='0.0.0.0')
    parser.add_argument('--port')
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument('--stop', action='store_true', help='Stop the matching bridge without stopping Unity.')
    actions.add_argument('--reconnect', action='store_true', help='Retire an idle CLI child; clients initialize again.')
    options = parser.parse_args(arguments)
    settings = resolve_settings(ROOT, Path.home(), os.environ)
    for warning in settings['warnings']:
        log(warning, True)
    try:
        value = float(options.port if options.port is not None else settings['port'])
        if not value.is_integer() or not 1 <= value <= 65535:
            raise ValueError('invalid port')
    except ValueError:
        log('--port must be an integer between 1 and 65535.', True)
        return 1
    if options.stop or options.reconnect:
        try:
            return control_bridge(settings, int(value), 'stop' if options.stop else 'reconnect')
        except (OSError, ValueError) as error:
            log('[unity-mcp] ' + str(error), True)
            return 1
    done = threading.Event()
    def stop(signum: int, frame: Optional[FrameType]) -> None:
        done.set()

    signals = [signal.SIGINT, signal.SIGTERM]
    if sys.platform == 'win32':
        signals.append(cast(signal.Signals, getattr(signal, 'SIGBREAK')))
    previous = {code: signal.signal(code, stop) for code in signals}
    try:
        return serve(ROOT, settings, int(value), options.host, done)
    except (OSError, ValueError) as error:
        log('[unity-mcp] ' + str(error), True)
        return 1
    finally:
        for code, handler in previous.items():
            signal.signal(code, handler)


if __name__ == '__main__':
    sys.exit(main())
