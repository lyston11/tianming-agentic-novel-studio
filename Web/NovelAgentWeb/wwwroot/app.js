const state = {
  page: document.body.dataset.page || "agent",
  workspace: null,
  storyBible: null,
  runs: [],
  materials: [],
  activeRun: null,
  activeWorkflowStage: "foundation",
  selectedMacroTitle: "",
  selectedCandidateTitle: ""
};

window.__novelAgentBootOk = false;
window.__novelAgentRuntimeErrors = [];
document.body.dataset.boot = "booting";

const markRuntimeError = message => {
  window.__novelAgentRuntimeErrors.push(message);
  document.body.dataset.boot = "failed";
  document.body.dataset.runtimeErrors = String(window.__novelAgentRuntimeErrors.length);
};

window.addEventListener("error", event => markRuntimeError(event.message));
window.addEventListener("unhandledrejection", event => {
  markRuntimeError(event.reason?.message || String(event.reason));
});

const $ = selector => document.querySelector(selector);
const $$ = selector => Array.from(document.querySelectorAll(selector));

const api = async (path, options = {}) => {
  const headers = options.body instanceof FormData ? {} : { "Content-Type": "application/json" };
  const response = await fetch(path, { headers, ...options });
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
  return response.json();
};

const post = (path, body = {}) => api(path, { method: "POST", body: JSON.stringify(body) });

const text = (value, fallback = "未设置") => {
  if (value === null || value === undefined) return fallback;
  const str = String(value).trim();
  return str.length ? str : fallback;
};

const safe = (selector, fn) => {
  const element = $(selector);
  if (element) fn(element);
};

const on = (selector, event, handler) => {
  const element = $(selector);
  if (element) element.addEventListener(event, handler);
};

const log = message => {
  const pane = $("#eventLog");
  if (!pane) return;
  const entry = document.createElement("div");
  entry.className = "log-line";
  entry.textContent = `${new Date().toLocaleTimeString()}  ${message}`;
  pane.prepend(entry);
};

const latestRun = () => {
  if (!state.runs.length) return null;
  return [...state.runs].sort((a, b) => new Date(b.updatedAt) - new Date(a.updatedAt))[0];
};

const isIntent = (run, name, numeric) => run?.intent === name || run?.intent === numeric;

const refresh = async () => {
  state.workspace = await api("/api/workspace");
  state.storyBible = await api("/api/story-bible");
  const runsResult = await api("/api/runs?take=80");
  const materialResult = await api("/api/materials");

  state.runs = runsResult.runs || state.storyBible.agentRuns || [];
  state.materials = materialResult.materials || [];

  const preferred = findPreferredRunForPage();
  if (!state.activeRun || !state.runs.some(r => r.runId === state.activeRun?.runId)) {
    state.activeRun = preferred || latestRun();
  } else {
    state.activeRun = state.runs.find(r => r.runId === state.activeRun.runId) || state.activeRun;
  }

  renderShell();
  renderPage();
};

const findPreferredRunForPage = () => {
  if (state.page === "workflow") {
    return state.runs.find(r => isIntent(r, "PlanChapter", 4))
      || state.runs.find(r => isIntent(r, "PlanVolumeArc", 3))
      || state.runs.find(r => isIntent(r, "CreateStoryFoundation", 1));
  }
  return latestRun();
};

const renderShell = () => {
  safe("#projectName", el => el.textContent = state.workspace?.projectName || "AgenticNovelStudio");
  safe("#constitutionState", el => el.textContent = state.storyBible?.constitution ? "已固化" : "未固化");
  safe("#materialCount", el => el.textContent = state.materials.length);
  safe("#runCount", el => el.textContent = state.runs.length);
  safe("#ledgerCount", el => {
    el.textContent = (state.storyBible?.canonLedger?.length || 0)
      + (state.storyBible?.foreshadowLedger?.length || 0)
      + (state.storyBible?.characterLedger?.length || 0);
  });
  $$("[data-nav]").forEach(link => link.classList.toggle("active", link.dataset.nav === state.page));
  renderActiveRun();
  renderStoryBibleSummary();
};

const renderPage = () => {
  if (state.page === "agent") renderAgentHome();
  if (state.page === "materials") renderMaterialsPage();
  if (state.page === "workflow") renderWorkflowPage();
};

const renderStoryBibleSummary = () => {
  const dl = $("#storyBibleSummary");
  if (!dl) return;
  const c = state.storyBible?.constitution;
  if (!c) {
    dl.innerHTML = `
      <dt>状态</dt><dd>尚未提交故事地基</dd>
      <dt>建议</dt><dd>先上传参考素材，或在创作工作流里生成故事地基。</dd>
    `;
    return;
  }

  dl.innerHTML = `
    <dt>类型承诺</dt><dd>${text(c.genre)} / ${text(c.subGenre)}</dd>
    <dt>核心钩子</dt><dd>${text(c.coreHook)}</dd>
    <dt>主冲突引擎</dt><dd>${text(c.mainConflictEngine)}</dd>
    <dt>禁止方向</dt><dd>${(c.forbiddenDirections || []).slice(0, 3).map(item => text(item)).join("；") || "未设置"}</dd>
  `;
};

const renderActiveRun = () => {
  safe("#activeRunTitle", el => {
    el.textContent = state.activeRun
      ? `${intentLabel(state.activeRun.intent)} / ${statusLabel(state.activeRun.status)}`
      : "等待规划";
  });
  safe("#stepTrack", el => {
    const steps = state.activeRun?.steps || [];
    el.innerHTML = steps.length
      ? steps.map(step => `
        <div class="step-item ${statusLabel(step.status)}">
          <span class="step-dot"></span>
          <div><strong>${text(step.name)}</strong><small>${text(step.purpose)} · ${text(step.riskLevel, "Low")}</small></div>
        </div>
      `).join("")
      : `<div class="empty">还没有可展示的 Agent Run。</div>`;
  });
};

const renderAgentHome = () => {
  const thread = $("#chatThread");
  if (!thread || thread.dataset.hydrated) return;
  thread.dataset.hydrated = "true";

  if (state.materials.length) {
    appendAgentMessage(`我已经看到素材库中有 ${state.materials.length} 份参考。你可以让我基于素材生成故事地基，或者指定某份素材影响卷规划。`);
  }
};

const renderMaterialsPage = () => {
  renderMaterialList("#materialList", state.materials, true);
};

const renderWorkflowPage = () => {
  renderWorkflowStage();
  renderMaterialList("#workflowMaterialRefs", state.materials.slice(0, 5), false);
  renderMacroCandidates(findRun("CreateStoryFoundation", 1) || state.activeRun);
  renderVolumeTimeline(findRun("PlanVolumeArc", 3) || state.activeRun);
  renderChapterCandidates(findRun("PlanChapter", 4) || state.activeRun);
};

const renderWorkflowStage = () => {
  if (state.page !== "workflow") return;
  $$("[data-flow-stage]").forEach(block => {
    const isActive = block.dataset.flowStage === state.activeWorkflowStage;
    block.classList.toggle("active", isActive);
    block.toggleAttribute("hidden", !isActive);
  });
  $$("[data-flow-target]").forEach(button => {
    const isActive = button.dataset.flowTarget === state.activeWorkflowStage;
    button.classList.toggle("active", isActive);
    button.setAttribute("aria-current", isActive ? "step" : "false");
  });
};

const setWorkflowStage = stage => {
  state.activeWorkflowStage = stage;
  renderWorkflowStage();
};

const findRun = (name, numeric) => state.runs.find(r => isIntent(r, name, numeric));

const renderMaterialList = (selector, materials, detailed) => {
  safe(selector, el => {
    el.innerHTML = materials.length
      ? materials.map(material => `
        <article class="material-card">
          <div>
            <strong>${text(material.fileName, "未命名素材")}</strong>
            <small>${text(material.sourceType)} · ${material.characterCount || 0} 字</small>
          </div>
          <p>${text(material.summary)}</p>
          <div class="score-line">${(material.tags || []).slice(0, 6).map(tag => `<span>${text(tag)}</span>`).join("")}</div>
          ${detailed ? `<ul>${(material.workflowReferences || []).map(item => `<li>${text(item)}</li>`).join("")}</ul>` : ""}
        </article>
      `).join("")
      : `<div class="empty">暂无素材。上传 txt / md / json / csv，或直接粘贴素材文本。</div>`;
  });
};

const appendUserMessage = message => {
  safe("#chatThread", thread => {
    thread.insertAdjacentHTML("beforeend", `<div class="agent-message user"><strong>你</strong><p>${escapeHtml(message)}</p></div>`);
    thread.scrollTop = thread.scrollHeight;
  });
};

const appendAgentMessage = (message, suggestions = []) => {
  safe("#chatThread", thread => {
    thread.insertAdjacentHTML("beforeend", `
      <div class="agent-message agent">
        <strong>天命 Agent</strong>
        <p>${escapeHtml(message).replace(/\n/g, "<br>")}</p>
        ${suggestions.length ? `<div class="suggestion-row">${suggestions.map(s => `<button class="ghost-button suggestion" type="button">${escapeHtml(s)}</button>`).join("")}</div>` : ""}
      </div>
    `);
    thread.scrollTop = thread.scrollHeight;
    $$(".suggestion").forEach(button => button.addEventListener("click", () => {
      const input = $("#chatInput");
      if (input) input.value = button.textContent || "";
    }));
  });
};

const escapeHtml = value => text(value, "").replace(/[&<>"']/g, char => ({
  "&": "&amp;",
  "<": "&lt;",
  ">": "&gt;",
  "\"": "&quot;",
  "'": "&#39;"
}[char]));

const renderMacroCandidates = run => {
  const container = $("#macroCandidates");
  if (!container) return;
  const candidates = run?.macroCandidates || [];
  if (!candidates.length) {
    container.innerHTML = `<div class="empty">等待生成宏观候选。素材库内容会作为故事地基参考。</div>`;
    return;
  }
  state.selectedMacroTitle ||= candidates[0].title;
  container.innerHTML = candidates.map(candidate => `
    <article class="concept-card ${candidate.title === state.selectedMacroTitle ? "selected" : ""}" data-macro="${candidate.title}">
      <h3>${text(candidate.title)}</h3>
      <p>${text(candidate.coreHook)}</p>
      <p><strong>规则：</strong>${text(candidate.worldCoreRule)}</p>
      <p><strong>深度：</strong>${text(candidate.depthLayer)}</p>
      <div class="score-line"><span>新鲜 ${candidate.noveltyScore}</span><span>续航 ${candidate.sustainabilityScore}</span><span>类型 ${candidate.typeMatchScore}</span></div>
    </article>
  `).join("");
  $$("[data-macro]").forEach(card => card.addEventListener("click", () => {
    state.selectedMacroTitle = card.dataset.macro;
    renderMacroCandidates(run);
    log(`已选择宏观候选：${state.selectedMacroTitle}`);
  }));
};

const renderVolumeTimeline = run => {
  const container = $("#volumeTimeline");
  if (!container) return;
  const plan = run?.volumeArcPlan || state.storyBible?.volumeArcs?.[0];
  const beats = plan?.chapterBeats || [];
  container.innerHTML = plan
    ? `
      <div class="concept-card selected"><h3>${text(plan.title)}</h3><p>${text(plan.volumePromise)}</p><p><strong>中点反转：</strong>${text(plan.midpointReversal)}</p><p><strong>高潮：</strong>${text(plan.climax)}</p></div>
      ${beats.map(beat => `<div class="beat-row"><span class="beat-index">${String(beat.index).padStart(2, "0")}</span><div><strong>${text(beat.role)}</strong><p>${text(beat.goal)}</p><small>${text(beat.turn)} / ${text(beat.cost)}</small></div></div>`).join("")}
    `
    : `<div class="empty">还没有卷级规划。先提交 Story Bible，再生成卷规划。</div>`;
};

const renderChapterCandidates = run => {
  const container = $("#chapterCandidates");
  if (!container) return;
  const brief = run?.chapterBrief;
  const candidates = brief?.candidates || [];
  if (!candidates.length) {
    container.innerHTML = `<div class="empty">等待生成章节候选。Agent 会结合 Story Bible、卷规划和创意知识库来做候选评审。</div>`;
    return;
  }
  state.selectedCandidateTitle ||= brief.recommendedCandidateTitle || candidates[0].title;
  container.innerHTML = candidates.map(candidate => `
    <article class="candidate-card ${candidate.title === state.selectedCandidateTitle ? "selected" : ""}" data-candidate="${candidate.title}">
      <h3>${text(candidate.title)}</h3>
      <p>${text(candidate.coreTwist)}</p>
      <p><strong>选择：</strong>${text(candidate.characterChoice)}</p>
      <p><strong>代价：</strong>${text(candidate.costOrConsequence)}</p>
      <div class="score-line"><span class="tag jade">总分 ${candidate.totalScore}</span><span>新鲜 ${candidate.noveltyScore}</span><span>戏剧 ${candidate.dramaScore}</span><span class="tag red">套路风险 ${candidate.clicheRisk}</span></div>
      <small>${text(candidate.recommendationReason)}</small>
    </article>
  `).join("");
  $$("[data-candidate]").forEach(card => card.addEventListener("click", () => {
    state.selectedCandidateTitle = card.dataset.candidate;
    renderChapterCandidates(run);
    log(`已选择章节候选：${state.selectedCandidateTitle}`);
  }));
};

const intentLabel = intent => ({
  1: "故事地基",
  3: "卷规划",
  4: "章节候选",
  CreateStoryFoundation: "故事地基",
  PlanVolumeArc: "卷规划",
  PlanChapter: "章节候选"
}[intent] || intent || "Run");

const statusLabel = status => ({
  0: "Draft",
  1: "Planning",
  2: "Retrieving",
  3: "AwaitingConfirmation",
  4: "Executing",
  5: "Validating",
  6: "Repairing",
  7: "Completed",
  8: "Failed",
  9: "Cancelled"
}[status] || status || "Unknown");

const bindActions = () => {
  on("#refreshBtn", "click", async () => { await refresh(); log("状态已刷新。"); });
  on("#continueBtn", "click", continueRun);
  on("#chatForm", "submit", sendChat);
  on("#ingestMaterialBtn", "click", ingestMaterial);
  on("#planFoundationBtn", "click", planFoundation);
  on("#commitFoundationBtn", "click", commitFoundation);
  on("#planVolumeBtn", "click", planVolume);
  on("#commitVolumeBtn", "click", commitVolume);
  on("#planChapterBtn", "click", planChapter);
  on("#selectCandidateBtn", "click", selectCandidate);
  on("#executeChapterBtn", "click", executeChapter);
  on("#importCanonBtn", "click", () => importLedger("canon"));
  on("#importForeshadowBtn", "click", () => importLedger("foreshadow"));
  on("#importCharacterBtn", "click", () => importLedger("character"));
  $$("[data-flow-target]").forEach(button => {
    button.addEventListener("click", () => setWorkflowStage(button.dataset.flowTarget));
  });
};

const sendChat = async event => {
  event.preventDefault();
  const input = $("#chatInput");
  const message = input?.value.trim();
  if (!message) return;
  appendUserMessage(message);
  input.value = "";
  const result = await post("/api/agent/chat", { message });
  appendAgentMessage(result.reply, result.suggestions || []);
};

const ingestMaterial = async () => {
  const fileInput = $("#materialFileInput");
  const file = fileInput?.files?.[0];
  const pasted = $("#materialTextInput")?.value || "";
  const fileText = file ? await file.text() : "";
  const content = fileText || pasted;
  if (!content.trim()) {
    log("素材内容为空。");
    return;
  }

  const result = await post("/api/materials", {
    fileName: file?.name || "pasted-material.txt",
    sourceType: $("#materialTypeInput")?.value || "Text",
    content
  });
  state.materials = result.materials || [];
  renderMaterialsPage();
  safe("#materialTextInput", el => el.value = "");
  if (fileInput) fileInput.value = "";
  log("素材已解析并写入素材库。");
};

const planFoundation = async () => {
  const materialHint = state.materials.slice(0, 3).map(m => `${m.fileName}: ${m.summary}`).join("\n");
  const seed = [$("#seedInput").value, materialHint ? `参考素材：\n${materialHint}` : ""]
    .filter(Boolean)
    .join("\n\n");
  const run = await post("/api/story-foundation", {
    userSeed: seed,
    genre: $("#genreInput").value,
    subGenre: $("#subGenreInput").value,
    targetReader: $("#readerInput").value,
    desiredDirection: $("#directionInput").value
  });
  state.activeRun = run;
  state.selectedMacroTitle = run.macroCandidates?.[0]?.title || "";
  setWorkflowStage("foundation");
  await refresh();
  log("已结合素材生成故事大框架候选。");
};

const commitFoundation = async () => {
  const run = findRun("CreateStoryFoundation", 1) || state.activeRun;
  if (!run?.runId) return log("没有可提交的大框架 Run。");
  const result = await post(`/api/story-foundation/${run.runId}/commit`, {
    overwrite: true,
    confirmed: true,
    selectedMacroCandidateTitle: state.selectedMacroTitle
  });
  await refresh();
  setWorkflowStage("volume");
  log(result.message || "Story Bible 已提交。");
};

const planVolume = async () => {
  const materialHint = state.materials.slice(0, 3).map(m => m.workflowReferences?.[1]).filter(Boolean).join("；");
  const run = await post("/api/volume-arc", {
    userGoal: [$("#volumeGoalInput").value, materialHint].filter(Boolean).join("；参考："),
    volumeId: $("#volumeIdInput").value,
    volumeTitle: $("#volumeTitleInput").value,
    startChapterId: $("#startChapterInput").value,
    endChapterId: $("#endChapterInput").value,
    expectedChapterCount: Number($("#chapterCountInput").value || 12)
  });
  state.activeRun = run;
  setWorkflowStage("volume");
  await refresh();
  log("已生成卷级规划。");
};

const commitVolume = async () => {
  const run = findRun("PlanVolumeArc", 3) || state.activeRun;
  if (!run?.runId) return log("没有可提交的卷规划 Run。");
  const result = await post(`/api/volume-arc/${run.runId}/commit`, { overwrite: false, confirmed: true });
  await refresh();
  setWorkflowStage("chapter");
  log(result.message || "卷规划已提交。");
};

const planChapter = async () => {
  const materialHint = state.materials.slice(0, 3).map(m => m.workflowReferences?.[2]).filter(Boolean).join("；");
  const run = await post("/api/chapter-plan", {
    chapterId: $("#chapterIdInput").value,
    userGoal: [$("#chapterGoalInput").value, materialHint].filter(Boolean).join("；参考：")
  });
  state.activeRun = run;
  state.selectedCandidateTitle = run.chapterBrief?.recommendedCandidateTitle || "";
  setWorkflowStage("chapter");
  await refresh();
  log("已生成章节候选。");
};

const selectCandidate = async () => {
  const run = findRun("PlanChapter", 4) || state.activeRun;
  if (!run?.runId) return log("没有可确认的章节候选 Run。");
  const result = await post(`/api/chapter-candidate/${run.runId}/select`, {
    candidateTitles: state.selectedCandidateTitle,
    selectionMode: "Single",
    selectionRationale: "用户在完整工作流中确认该候选。",
    confirmed: true
  });
  state.activeRun = result.run || run;
  await refresh();
  log(result.message || "章节候选已确认。");
};

const executeChapter = async () => {
  const run = findRun("PlanChapter", 4) || state.activeRun;
  if (!run?.runId) return log("没有可执行的章节 Run。");
  const result = await post(`/api/chapter/${run.runId}/execute`, { confirmed: true });
  state.activeRun = result.run || run;
  await refresh();
  log(result.message || "章节生成流程已执行。");
};

const continueRun = async () => {
  const run = state.activeRun;
  if (!run?.runId) return log("没有可推进的 Agent Run。");
  const result = await post(`/api/run/${run.runId}/continue`, { maxAutoRisk: "Medium" });
  state.activeRun = result.run || run;
  await refresh();
  log(result.message || "Agent Run 已推进。");
};

const importLedger = async kind => {
  const run = findRun("PlanChapter", 4) || state.activeRun;
  if (!run?.runId) return log("没有可导入账本的章节 Run。");
  const result = await post(`/api/run/${run.runId}/${kind}/import`);
  state.activeRun = result.run || run;
  await refresh();
  log(result.message || `${kind} 账本导入完成。`);
};

const boot = async () => {
  bindActions();
  await refresh();
  window.__novelAgentBootOk = true;
  document.body.dataset.boot = "ready";
  document.body.dataset.runtimeErrors = String(window.__novelAgentRuntimeErrors.length);
  log("页面已就绪。");
};

boot().catch(error => {
  console.error(error);
  markRuntimeError(error.message);
  log(`启动失败：${error.message}`);
});
