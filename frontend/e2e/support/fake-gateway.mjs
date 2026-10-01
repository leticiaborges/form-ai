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

    const chunks = [];
    req.on("data", (c) => chunks.push(c));

    req.on("end", () => {
      const body = Buffer.concat(chunks).toString("utf8");

      // Scenarios are picked by a marker in the pasted source text.
      if (body.includes("[fake:down]"))
        return send(res, 503, { error: { message: "Gateway down" } });
      if (body.includes("[fake:slow]")) return; // never answers; the client timeout fires

      const reply = (finish_reason, content) =>
        send(res, 200, {
          id: "chatcmpl-fake",
          object: "chat.completion",
          choices: [{ index: 0, finish_reason, message: { role: "assistant", content } }],
          usage: { prompt_tokens: 10, completion_tokens: 10, total_tokens: 20 },
        });

      if (body.includes("[fake:truncated]"))
        return reply("length", JSON.stringify(draft).slice(0, 60));
      if (body.includes("[fake:invalid]")) return reply("stop", JSON.stringify({ questions: [] }));

      reply("stop", JSON.stringify(draft));
    });

    return;
  }

  send(res, 404, { error: { message: "Not found" } });
});

server.listen(port, "127.0.0.1", () => console.log(`fake gateway on http://127.0.0.1:${port}`));
