"""
Metis Agent Service IPC Server
Runs a lightweight async local HTTP/JSON server allowing Metis C# desktop app to orchestrate tasks, safety checks, and browser automation via Python.
"""
import json
import asyncio
from http.server import HTTPServer, BaseHTTPRequestHandler
from models.agent_models import Plan, Action, ActionType, RiskLevel, Target
from browser.browser_agent import MetisBrowserAgent

browser_agent = MetisBrowserAgent()

class MetisIPCHandler(BaseHTTPRequestHandler):
    def _set_headers(self, status=200):
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()

    def do_GET(self):
        if self.path == '/health':
            self._set_headers(200)
            self.wfile.write(json.dumps({"status": "healthy", "service": "Metis.AgentService"}).encode('utf-8'))
        else:
            self._set_headers(404)

    def do_POST(self):
        content_length = int(self.headers.get('Content-Length', 0))
        body = self.rfile.read(content_length)
        data = json.loads(body.decode('utf-8')) if body else {}

        if self.path == '/plan':
            goal = data.get('goal', '')
            plan = Plan(
                task_id="task-001",
                goal=goal,
                subgoals=["Inspect UI", "Execute Action", "Verify Outcome"],
                actions=[
                    Action(
                        type=ActionType.INSPECT_SCREEN,
                        reason="Observe current desktop state",
                        risk=RiskLevel.LOW
                    )
                ],
                summary=f"Plan generated for: {goal}"
            )
            self._set_headers(200)
            self.wfile.write(plan.model_dump_json().encode('utf-8'))

        elif self.path == '/browser/navigate':
            url = data.get('url', 'https://example.com')
            loop = asyncio.new_event_loop()
            asyncio.set_event_loop(loop)
            result = loop.run_until_complete(browser_agent.navigate(url))
            loop.close()
            self._set_headers(200)
            self.wfile.write(json.dumps(result).encode('utf-8'))

        else:
            self._set_headers(404)

def run_server(port=18790):
    server_address = ('127.0.0.1', port)
    httpd = HTTPServer(server_address, MetisIPCHandler)
    print(f"Metis Agent Service listening on http://127.0.0.1:{port}")
    httpd.serve_forever()

if __name__ == '__main__':
    run_server()
