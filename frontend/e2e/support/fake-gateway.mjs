import http from "node:http";

const port = Number(process.env.GATEWAY_PORT ?? 4055);
const appKey = process.env.GATEWAY_FAKEKEY ?? "sk-fake-gateway-key";

const draft = {
  questions: [
    {
      text: "What is the capital of France?",
      type: "Single",
      correctAnswer: null,
      options: [
        { text: "Paris", isCorrect: true },
        { text: "Lyon", isCorrect: false },
        { text: "Marseille", isCorrect: false },
      ],
    },
    { text: "Why?", type: "Text", correctAnswer: "Because", options: [] },
  ],
};

function send(res, status, body) {
  res.writeHead(status, { "Content-Type": "application/json" });
  res.end(JSON.stringify(body));
}

const server = http.createServer((req, res) => {
  if (req.method === "GET" && req.url === "/health") return send(res, 200, { status: "ok" });

  if (req.method === "POST" && req.url === "/v1/chat/completions") {
    if (req.headers.authorization !== `Bearer ${appKey}`) {
      return send(res, 401, { error: "Unauthorized" });
    }

    req.resume();

    req.on("end", () => {
      send(res, 200, {
        id: "chatcmpl-fake",
        object: "chat.completion",
        choices: [
          {
            index: 0,
            finish_reason: "stop",
            message: { role: "assistant", content: JSON.stringify(draft) },
          },
        ],
        usage: { prompt_tokens: 10, completion_tokens: 10, total_tokens: 20 },
      });
    });

    return;
  }

  send(res, 404, { error: { message: "Not found" } });
});

server.listen(port, "127.0.0.1", () => console.log(`fake gateway on http://127.0.0.1:${port}`));
