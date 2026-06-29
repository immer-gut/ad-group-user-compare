const state = {
  results: [],
  filtered: [],
  comparison: [],
  showingComparison: false
};

const columns = [
  "groupName",
  "groupPath",
  "samAccountName",
  "displayName",
  "mail",
  "enabled",
  "department",
  "title",
  "distinguishedName"
];

const comparisonColumns = [
  "status",
  "groupName",
  "userA",
  "userB",
  "groupPathA",
  "groupPathB"
];

const elements = {
  form: document.querySelector("#searchForm"),
  groupPattern: document.querySelector("#groupPattern"),
  searchBase: document.querySelector("#searchBase"),
  server: document.querySelector("#server"),
  onlyEnabled: document.querySelector("#onlyEnabled"),
  searchButton: document.querySelector("#searchButton"),
  testLdapButton: document.querySelector("#testLdapButton"),
  summary: document.querySelector("#summary"),
  filter: document.querySelector("#filterInput"),
  table: document.querySelector("#resultsTable"),
  tbody: document.querySelector("#resultsTable tbody"),
  copyGroups: document.querySelector("#copyGroupsButton"),
  export: document.querySelector("#exportButton"),
  userA: document.querySelector("#userA"),
  userB: document.querySelector("#userB"),
  userOptions: document.querySelector("#userOptions"),
  compare: document.querySelector("#compareButton"),
  clearCompare: document.querySelector("#clearCompareButton"),
  toast: document.querySelector("#toast"),
  connectionSummary: document.querySelector("#connectionSummary"),
  themeButton: document.querySelector("#themeButton"),
  ldapTestDialog: document.querySelector("#ldapTestDialog"),
  ldapTestConfig: document.querySelector("#ldapTestConfig"),
  ldapTestResults: document.querySelector("#ldapTestResults"),
  runLdapTestButton: document.querySelector("#runLdapTestButton")
};

init();

function init() {
  loadTheme();
  loadConfig();
  elements.form.addEventListener("submit", search);
  elements.filter.addEventListener("input", applyFilter);
  elements.copyGroups.addEventListener("click", copyGroups);
  elements.export.addEventListener("click", exportCsv);
  elements.compare.addEventListener("click", compareUsers);
  elements.clearCompare.addEventListener("click", clearComparison);
  elements.themeButton.addEventListener("click", toggleTheme);
  elements.testLdapButton.addEventListener("click", openLdapTestDialog);
  elements.runLdapTestButton.addEventListener("click", runLdapTest);
  renderRows([]);
}

async function loadConfig() {
  const response = await fetch("/api/config");
  const config = await readJsonResponse(response, "Konfiguration konnte nicht geladen werden.");
  elements.server.value = config.server ?? "";
  elements.searchBase.value = config.searchBase ?? "";

  const server = config.server || "kein Server gesetzt";
  const searchBase = config.searchBase || "keine SearchBase gesetzt";
  const ssl = config.useSsl ? "LDAPS" : "LDAP";
  const bind = config.bindConfigured ? "Bind konfiguriert" : "kein Bind-DN";
  elements.connectionSummary.textContent = `${ssl} · ${server} · ${searchBase} · ${bind}`;
}

async function search(event) {
  event.preventDefault();
  setBusy(true);
  setSummary("Suche laeuft...");
  clearComparison();

  try {
    const payload = {
      groupPattern: elements.groupPattern.value.trim(),
      searchBase: elements.searchBase.value.trim(),
      server: elements.server.value.trim(),
      onlyEnabled: elements.onlyEnabled.checked
    };

    const response = await fetch("/api/search", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });

    const body = await readJsonResponse(response, "LDAP-Suche fehlgeschlagen.");
    if (!response.ok) {
      throw new Error(body.error ?? "LDAP-Suche fehlgeschlagen.");
    }

    state.results = body.results ?? [];
    state.filtered = [...state.results];
    elements.filter.value = "";
    updateSummary(body.groupCount ?? 0, body.userCount ?? 0);
    renderRows(state.filtered);
    await refreshUserOptions();
    updateButtons();
  } catch (error) {
    state.results = [];
    state.filtered = [];
    renderRows([]);
    setSummary(error.message, true);
    showToast(error.message);
    updateButtons();
  } finally {
    setBusy(false);
  }
}

function applyFilter() {
  const filter = elements.filter.value.trim().toLocaleLowerCase();
  const source = state.showingComparison ? state.comparison : state.results;
  const activeColumns = state.showingComparison ? comparisonColumns : columns;

  state.filtered = filter
    ? source.filter(row => activeColumns.some(column => String(row[column] ?? "").toLocaleLowerCase().includes(filter)))
    : [...source];

  renderRows(state.filtered);
  updateButtons();
}

async function refreshUserOptions() {
  const response = await fetch("/api/users", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(state.results)
  });
  const users = await readJsonResponse(response, "Benutzerliste konnte nicht erstellt werden.");
  elements.userOptions.replaceChildren(...users.map(user => {
    const option = document.createElement("option");
    option.value = user;
    return option;
  }));
}

async function compareUsers() {
  const payload = {
    userA: elements.userA.value.trim(),
    userB: elements.userB.value.trim(),
    results: state.results
  };

  const response = await fetch("/api/compare", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload)
  });

  const body = await readJsonResponse(response, "Vergleich fehlgeschlagen.");
  if (!response.ok) {
    showToast(body.error ?? "Vergleich fehlgeschlagen.");
    return;
  }

  state.comparison = body;
  state.showingComparison = true;
  elements.filter.value = "";
  state.filtered = [...state.comparison];
  renderComparisonRows(state.filtered);
  setSummary(`${state.comparison.length} Vergleichszeilen`);
  updateButtons();
}

function openLdapTestDialog() {
  renderLdapTestConfig();
  elements.ldapTestResults.replaceChildren();
  if (typeof elements.ldapTestDialog.showModal === "function") {
    elements.ldapTestDialog.showModal();
  } else {
    elements.ldapTestDialog.setAttribute("open", "");
  }
}

async function runLdapTest() {
  elements.runLdapTestButton.disabled = true;
  elements.runLdapTestButton.textContent = "Test läuft...";
  elements.ldapTestResults.replaceChildren(renderTestPlaceholder("LDAP-Test läuft..."));

  try {
    const response = await fetch("/api/test-ldap", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        groupPattern: elements.groupPattern.value.trim(),
        searchBase: elements.searchBase.value.trim(),
        server: elements.server.value.trim()
      })
    });
    const body = await readJsonResponse(response, "LDAP-Test fehlgeschlagen.");
    if (!response.ok) {
      throw new Error(body.error ?? "LDAP-Test fehlgeschlagen.");
    }

    renderLdapTestResult(body);
  } catch (error) {
    elements.ldapTestResults.replaceChildren(renderTestStep({
      name: "Test",
      success: false,
      message: error.message,
      detail: null
    }));
  } finally {
    elements.runLdapTestButton.disabled = false;
    elements.runLdapTestButton.textContent = "Test starten";
  }
}

function renderLdapTestConfig() {
  const items = [
    ["Server", elements.server.value.trim() || "aus Stack-Konfiguration"],
    ["Port", "aus Stack-Konfiguration"],
    ["SSL", "aus Stack-Konfiguration"],
    ["SearchBase", elements.searchBase.value.trim() || "aus Stack-Konfiguration"],
    ["Gruppenmuster", elements.groupPattern.value.trim() || "nicht gesetzt"],
    ["Bind", "aus Stack-Konfiguration"]
  ];

  elements.ldapTestConfig.replaceChildren(...items.map(([label, value]) => {
    const item = document.createElement("div");
    const strong = document.createElement("strong");
    const span = document.createElement("span");
    strong.textContent = label;
    span.textContent = value;
    item.append(strong, span);
    return item;
  }));
}

function renderLdapTestResult(result) {
  const configItems = [
    ["Server", result.server || "nicht gesetzt"],
    ["Port", String(result.port)],
    ["SSL", result.useSsl ? "ja" : "nein"],
    ["SearchBase", result.searchBase || "nicht gesetzt"],
    ["Bind", result.bindConfigured ? "konfiguriert" : "anonym"]
  ];

  elements.ldapTestConfig.replaceChildren(...configItems.map(([label, value]) => {
    const item = document.createElement("div");
    const strong = document.createElement("strong");
    const span = document.createElement("span");
    strong.textContent = label;
    span.textContent = value;
    item.append(strong, span);
    return item;
  }));

  elements.ldapTestResults.replaceChildren(...(result.steps ?? []).map(renderTestStep));
}

function renderTestPlaceholder(message) {
  return renderTestStep({ name: "Status", success: true, message, detail: null });
}

function renderTestStep(step) {
  const section = document.createElement("section");
  section.className = `test-step ${step.success ? "ok" : "fail"}`;
  const strong = document.createElement("strong");
  const message = document.createElement("p");
  strong.textContent = `${step.success ? "OK" : "Fehler"} · ${step.name}`;
  message.textContent = step.message ?? "";
  section.append(strong, message);

  if (step.detail) {
    const detail = document.createElement("p");
    detail.className = "detail";
    detail.textContent = step.detail;
    section.append(detail);
  }

  return section;
}

function clearComparison() {
  if (!state.showingComparison) {
    return;
  }

  state.showingComparison = false;
  state.comparison = [];
  elements.filter.value = "";
  state.filtered = [...state.results];
  renderRows(state.filtered);
  setSummary(`${state.results.length} Ergebniszeilen`);
  updateButtons();
}

function renderRows(rows) {
  setHeaders(["GroupName", "GroupPath", "SamAccountName", "DisplayName", "Mail", "Enabled", "Department", "Title", "DistinguishedName"]);
  elements.tbody.replaceChildren(...rows.map(row => renderRow(columns, row)));
}

function renderComparisonRows(rows) {
  setHeaders(["Status", "GroupName", "User 1", "User 2", "GroupPath User 1", "GroupPath User 2"]);
  elements.tbody.replaceChildren(...rows.map(row => renderRow(comparisonColumns, row)));
}

function setHeaders(headers) {
  const headerRow = elements.table.querySelector("thead tr");
  headerRow.replaceChildren(...headers.map(header => {
    const th = document.createElement("th");
    th.textContent = header;
    return th;
  }));
}

function renderRow(activeColumns, row) {
  const tr = document.createElement("tr");
  tr.replaceChildren(...activeColumns.map(column => {
    const td = document.createElement("td");
    const value = row[column];
    td.textContent = typeof value === "boolean" ? (value ? "True" : "False") : (value ?? "");
    td.title = td.textContent;
    return td;
  }));
  return tr;
}

async function copyGroups() {
  const groupNames = visibleGroupNames();
  await navigator.clipboard.writeText(groupNames.join("\n"));
  showToast(`${groupNames.length} Gruppen kopiert.`);
}

function exportCsv() {
  const activeColumns = state.showingComparison ? comparisonColumns : columns;
  const header = state.showingComparison
    ? ["Status", "GroupName", "UserA", "UserB", "GroupPathA", "GroupPathB"]
    : ["GroupName", "GroupPath", "SamAccountName", "DisplayName", "Mail", "Enabled", "Department", "Title", "DistinguishedName"];
  const rows = [header, ...state.filtered.map(row => activeColumns.map(column => row[column] ?? ""))];
  const csv = rows.map(row => row.map(formatCsvValue).join(";")).join("\r\n");
  const blob = new Blob([csv], { type: "text/csv;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  const prefix = state.showingComparison ? "ad-user-group-comparison" : "ad-users";
  link.href = url;
  link.download = `${prefix}-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

function formatCsvValue(value) {
  const text = String(value).replaceAll('"', '""');
  return /[;"\r\n]/.test(text) ? `"${text}"` : text;
}

function visibleGroupNames() {
  return [...new Set(state.filtered.map(row => row.groupName).filter(Boolean))]
    .sort((a, b) => a.localeCompare(b));
}

function updateSummary(groupCount, userCount) {
  setSummary(`${state.results.length} Zeilen · ${groupCount} Gruppen · ${userCount} Benutzer`);
}

function setSummary(text, error = false) {
  elements.summary.textContent = text;
  elements.summary.classList.toggle("status-error", error);
}

function updateButtons() {
  const hasRows = state.filtered.length > 0;
  const hasResults = state.results.length > 0;
  elements.copyGroups.disabled = !hasRows;
  elements.export.disabled = !hasRows;
  elements.compare.disabled = !hasResults;
  elements.clearCompare.disabled = !state.showingComparison;
}

function setBusy(isBusy) {
  elements.searchButton.disabled = isBusy;
  elements.searchButton.textContent = isBusy ? "Sucht..." : "Suchen";
}

function showToast(message) {
  elements.toast.textContent = message;
  elements.toast.classList.add("visible");
  window.clearTimeout(showToast.timeout);
  showToast.timeout = window.setTimeout(() => elements.toast.classList.remove("visible"), 3200);
}

async function readJsonResponse(response, fallbackMessage) {
  const text = await response.text();
  if (!text) {
    return response.ok ? {} : { error: `${fallbackMessage} HTTP ${response.status}` };
  }

  try {
    return JSON.parse(text);
  } catch {
    return response.ok ? {} : { error: `${fallbackMessage} HTTP ${response.status}: ${text.slice(0, 180)}` };
  }
}

function loadTheme() {
  const theme = localStorage.getItem("ad-group-user-compare-theme") ?? "light";
  document.documentElement.dataset.theme = theme;
}

function toggleTheme() {
  const current = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = current;
  localStorage.setItem("ad-group-user-compare-theme", current);
}
