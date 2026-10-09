const ball = document.getElementById("ball");
const ballAnswer = document.getElementById("ballAnswer");
const form = document.getElementById("askForm");
const input = document.getElementById("question");
const button = document.getElementById("askButton");
const status = document.getElementById("status");
const moodBadge = document.getElementById("moodBadge");
const historyList = document.getElementById("history");

const connection = new signalR.HubConnectionBuilder()
  .withUrl("/hub/oracle")
  .withAutomaticReconnect()
  .build();

connection.on("ProphecyRevealed", (payload) => {
  reveal(payload.text, payload.kind);
  prependHistory(lastQuestion, payload.text, payload.kind);
  setBusy(false);
});

connection.on("Rejected", (payload) => {
  status.textContent = payload.reason;
  ball.classList.remove("shaking");
  setBusy(false);
});

let lastQuestion = "";

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  const question = input.value.trim();
  if (!question) return;

  lastQuestion = question;
  status.textContent = "";
  ballAnswer.classList.remove("visible");
  ball.classList.add("shaking");
  setBusy(true);

  try {
    await connection.invoke("Ask", question);
  } catch (err) {
    status.textContent = "Could not reach the cosmos. Try again.";
    ball.classList.remove("shaking");
    setBusy(false);
  }
});

function reveal(text, kind) {
  ball.classList.remove("shaking");
  ballAnswer.textContent = text;
  ballAnswer.className = "ball-answer visible " + kind;
}

function setBusy(busy) {
  button.disabled = busy;
  input.disabled = busy;
}

function prependHistory(question, answer, kind) {
  const li = document.createElement("li");
  li.innerHTML = `<div class="q">${escapeHtml(question)}</div><div class="a ${kind}">${escapeHtml(answer)}</div>`;
  historyList.prepend(li);
  while (historyList.children.length > 20) {
    historyList.removeChild(historyList.lastChild);
  }
}

function escapeHtml(str) {
  const div = document.createElement("div");
  div.textContent = str;
  return div.innerHTML;
}

async function loadMood() {
  try {
    const res = await fetch("/api/v1/mood");
    const data = await res.json();
    moodBadge.textContent = `Oracle mood: ${data.mood}`;
  } catch {
    moodBadge.textContent = "";
  }
}

async function loadHistory() {
  try {
    const res = await fetch("/api/v1/history?take=20");
    const entries = await res.json();
    for (const entry of entries) {
      prependHistoryAtEnd(entry.question, entry.answer, entry.kind);
    }
  } catch {
    /* history is a nice-to-have; ignore failures */
  }
}

function prependHistoryAtEnd(question, answer, kind) {
  const li = document.createElement("li");
  li.innerHTML = `<div class="q">${escapeHtml(question)}</div><div class="a ${kind}">${escapeHtml(answer)}</div>`;
  historyList.appendChild(li);
}

connection.start().catch(() => {
  status.textContent = "Could not connect to the Oracle. Refresh to retry.";
});

loadMood();
loadHistory();
