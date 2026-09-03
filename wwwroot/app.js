const state = {
  results: [],
  filtered: [],
  groupNames: [],
  groupCount: 0,
  userCount: 0,
  comparison: [],
  showingComparison: false,
  config: null
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
  "userB"
];

const recentGroupPatternsKey = "ad-group-user-compare-recent-group-patterns";
const maxRecentGroupPatterns = 10;

const elements = {
  form: document.querySelector("#searchForm"),
  groupPattern: document.querySelector("#groupPattern"),
  groupPatternOptions: document.querySelector("#groupPatternOptions"),
  groupPatternHistory: document.querySelector("#groupPatternHistory"),
  clearGroupPattern: document.querySelector("#clearGroupPatternButton"),
  deleteGroupPattern: document.querySelector("#deleteGroupPatternButton"),
  searchBase: document.querySelector("#searchBase"),
  server: document.querySelector("#server"),
  onlyEnabled: document.querySelector("#onlyEnabled"),
  searchButton: document.querySelector("#searchButton"),
  testLdapButton: document.querySelector("#testLdapButton"),
  summary: document.querySelector("#summary"),
  filter: document.querySelector("#filterInput"),
  clearFilter: document.querySelector("#clearFilterButton"),
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
  appVersion: document.querySelector("#appVersion"),
  connectionSummary: document.querySelector("#connectionSummary"),
  themeButton: document.querySelector("#themeButton"),
  ldapTestDialog: document.querySelector("#ldapTestDialog"),
  ldapTestConfig: document.querySelector("#ldapTestConfig"),
  ldapTestResults: document.querySelector("#ldapTestResults"),
  runLdapTestButton: document.querySelector("#runLdapTestButton"),
  saveLdapSettingsButton: document.querySelector("#saveLdapSettingsButton"),
  ldapServer: document.querySelector("#ldapServerInput"),
  ldapPort: document.querySelector("#ldapPortInput"),
  ldapUseSsl: document.querySelector("#ldapUseSslInput"),
  ldapUseStartTls: document.querySelector("#ldapUseStartTlsInput"),
  ldapVerifyCertificate: document.querySelector("#ldapVerifyCertificateInput"),
  ldapSearchBase: document.querySelector("#ldapSearchBaseInput"),
  ldapGroupPattern: document.querySelector("#ldapGroupPatternInput"),
  ldapBindDn: document.querySelector("#ldapBindDnInput"),
  ldapBindPassword: document.querySelector("#ldapBindPasswordInput"),
  ldapClearPassword: document.querySelector("#ldapClearPasswordInput"),
  ldapUsePaging: document.querySelector("#ldapUsePagingInput"),
  copyGroupsDialog: document.querySelector("#copyGroupsDialog"),
  copyGroupsText: document.querySelector("#copyGroupsText")
};

init();

function init() {
  loadTheme();
  loadVersion();
  loadConfig();
  elements.form.addEventListener("submit", search);
  elements.groupPattern.addEventListener("input", updateGroupPatternButton);
  elements.groupPatternHistory.addEventListener("change", selectGroupPatternHistory);
  elements.clearGroupPattern.addEventListener("click", clearGroupPattern);
  elements.deleteGroupPattern.addEventListener("click", deleteCurrentGroupPattern);
  elements.filter.addEventListener("input", applyFilter);
  elements.clearFilter.addEventListener("click", clearFilter);
  elements.copyGroups.addEventListener("click", copyGroups);
  elements.export.addEventListener("click", exportCsv);
  elements.compare.addEventListener("click", compareUsers);
  elements.clearCompare.addEventListener("click", clearComparison);
  elements.themeButton.addEventListener("click", toggleTheme);
  elements.testLdapButton.addEventListener("click", openLdapTestDialog);
  elements.runLdapTestButton.addEventListener("click", runLdapTest);
  elements.saveLdapSettingsButton.addEventListener("click", saveLdapSettings);
  elements.ldapUseSsl.addEventListener("change", keepSingleTlsMode);
  elements.ldapUseStartTls.addEventListener("change", keepSingleTlsMode);
  renderRecentGroupPatterns();
  updateGroupPatternButton();
  updateFilterClearButton();
  renderRows([]);
}

async function loadVersion() {
  try {
    const response = await fetch("/api/version");
    const body = await readJsonResponse(response, "Version konnte nicht geladen werden.");
    if (!response.ok || !body.version) {
      throw new Error(body.error ?? "Version konnte nicht geladen werden.");
    }

    elements.appVersion.textContent = `Version ${body.version}`;
  } catch {
    elements.appVersion.textContent = "Version unbekannt";
  }
}

async function loadConfig() {
  const response = await fetch("/api/config");
  const config = await readJsonResponse(response, "Konfiguration konnte nicht geladen werden.");
  state.config = config;
  applyConfigToSearchFields(config);
  updateConnectionSummary(config);
}

function applyConfigToSearchFields(config) {
  elements.server.value = config.server ?? "";
  elements.searchBase.value = config.searchBase ?? "";
  if (!elements.groupPattern.value && config.groupPattern) {
    elements.groupPattern.value = config.groupPattern;
    updateGroupPatternButton();
  }
}

function updateConnectionSummary(config) {
  const server = config.server || "kein Server gesetzt";
  const searchBase = config.searchBase || "keine SearchBase gesetzt";
  const ssl = config.useSsl ? "LDAPS" : (config.useStartTls ? "LDAP+StartTLS" : "LDAP");
  const bind = config.bindConfigured ? "Bind konfiguriert" : "kein Bind-DN";
  const certificate = config.verifyCertificate === false ? "Zertifikat nicht geprüft" : "Zertifikat geprüft";
  const saved = config.settingsSaved ? "gespeichert" : "Stack-Defaults";
  elements.connectionSummary.textContent = `${ssl} · ${server}:${config.port ?? ""} · ${searchBase} · ${bind} · ${certificate} · ${saved}`;
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
    state.groupNames = body.groupNames ?? [];
    state.groupCount = body.groupCount ?? 0;
    state.userCount = body.userCount ?? 0;
    state.filtered = [...state.results];
    elements.filter.value = "";
    updateFilterClearButton();
    rememberGroupPattern(payload.groupPattern);
    updateSummary();
    renderRows(state.filtered);
    await refreshUserOptions();
    updateButtons();
  } catch (error) {
    state.results = [];
    state.filtered = [];
    state.groupNames = [];
    state.groupCount = 0;
    state.userCount = 0;
    elements.filter.value = "";
    updateFilterClearButton();
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
  updateFilterSummary(filter);
  updateFilterClearButton();
  updateButtons();
}

function clearFilter() {
  elements.filter.value = "";
  applyFilter();
  elements.filter.focus();
}

function updateFilterClearButton() {
  elements.clearFilter.disabled = elements.filter.value.trim().length === 0;
}

function readRecentGroupPatterns() {
  try {
    const parsed = JSON.parse(localStorage.getItem(recentGroupPatternsKey) ?? "[]");
    return Array.isArray(parsed)
      ? parsed.filter(pattern => typeof pattern === "string" && pattern.trim().length > 0)
      : [];
  } catch {
    return [];
  }
}

function saveRecentGroupPatterns(patterns) {
  try {
    localStorage.setItem(recentGroupPatternsKey, JSON.stringify(patterns.slice(0, maxRecentGroupPatterns)));
  } catch {
    // Browser storage can be disabled; search should still work.
  }
}

function rememberGroupPattern(pattern) {
  const normalized = pattern.trim();
  if (!normalized) {
    return;
  }

  const existing = readRecentGroupPatterns()
    .filter(item => item.toLocaleLowerCase() !== normalized.toLocaleLowerCase());
  saveRecentGroupPatterns([normalized, ...existing]);
  renderRecentGroupPatterns();
  updateGroupPatternButton();
}

function renderRecentGroupPatterns() {
  const patterns = readRecentGroupPatterns();
  elements.groupPatternOptions.replaceChildren(...patterns.map(pattern => {
    const option = document.createElement("option");
    option.value = pattern;
    return option;
  }));

  const placeholder = document.createElement("option");
  placeholder.value = "";
  placeholder.textContent = patterns.length === 0 ? "Keine Muster gespeichert" : "Muster auswählen";

  elements.groupPatternHistory.replaceChildren(placeholder, ...patterns.map(pattern => {
    const option = document.createElement("option");
    option.value = pattern;
    option.textContent = pattern;
    return option;
  }));
  elements.groupPatternHistory.disabled = patterns.length === 0;
  elements.groupPatternHistory.value = "";
}

function selectGroupPatternHistory() {
  const pattern = elements.groupPatternHistory.value;
  if (!pattern) {
    return;
  }

  elements.groupPattern.value = pattern;
  updateGroupPatternButton();
}

function deleteCurrentGroupPattern() {
  const pattern = elements.groupPattern.value.trim();
  const before = readRecentGroupPatterns();
  const remaining = readRecentGroupPatterns()
    .filter(item => item.toLocaleLowerCase() !== pattern.toLocaleLowerCase());

  saveRecentGroupPatterns(remaining);
  elements.groupPattern.value = "";
  renderRecentGroupPatterns();
  updateGroupPatternButton();
  showToast(before.length === remaining.length ? "Musterfeld geleert." : `Muster "${pattern}" gelöscht.`);
}

function clearGroupPattern() {
  elements.groupPattern.value = "";
  elements.groupPatternHistory.value = "";
  updateGroupPatternButton();
  elements.groupPattern.focus();
}

function updateGroupPatternButton() {
  const hasPattern = elements.groupPattern.value.trim().length > 0;
  elements.clearGroupPattern.disabled = !hasPattern;
  elements.deleteGroupPattern.disabled = !hasPattern;

  if (elements.groupPatternHistory.value && elements.groupPatternHistory.value !== elements.groupPattern.value) {
    elements.groupPatternHistory.value = "";
  }
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
  updateFilterClearButton();
  state.filtered = [...state.comparison];
  renderComparisonRows(state.filtered);
  updateFilterSummary("");
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
      body: JSON.stringify(readLdapSettingsForm())
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
  const config = state.config ?? {};
  elements.ldapServer.value = elements.server.value.trim() || config.server || "";
  elements.ldapPort.value = String(config.port ?? 636);
  elements.ldapUseSsl.checked = Boolean(config.useSsl);
  elements.ldapUseStartTls.checked = Boolean(config.useStartTls);
  normalizeTlsPort();
  elements.ldapVerifyCertificate.checked = config.verifyCertificate !== false;
  elements.ldapSearchBase.value = elements.searchBase.value.trim() || config.searchBase || "";
  elements.ldapGroupPattern.value = elements.groupPattern.value.trim() || config.groupPattern || "";
  elements.ldapBindDn.value = config.bindDn || "";
  elements.ldapBindPassword.value = "";
  elements.ldapBindPassword.placeholder = config.bindPasswordConfigured
    ? "gesetzt - leer lassen zum Beibehalten"
    : "leer/nicht gesetzt";
  elements.ldapClearPassword.checked = false;
  elements.ldapUsePaging.checked = config.usePaging !== false;
}

function renderLdapTestResult(result) {
  elements.ldapTestResults.replaceChildren(...(result.steps ?? []).map(renderTestStep));
}

async function saveLdapSettings() {
  elements.saveLdapSettingsButton.disabled = true;
  elements.saveLdapSettingsButton.textContent = "Speichert...";

  try {
    const response = await fetch("/api/config", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(readLdapSettingsForm())
    });
    const config = await readJsonResponse(response, "LDAP-Einstellungen konnten nicht gespeichert werden.");
    if (!response.ok) {
      throw new Error(config.error ?? "LDAP-Einstellungen konnten nicht gespeichert werden.");
    }

    state.config = config;
    applyConfigToSearchFields(config);
    renderLdapTestConfig();
    updateConnectionSummary(config);
    showToast("LDAP-Einstellungen gespeichert.");
  } catch (error) {
    showToast(error.message);
    elements.ldapTestResults.replaceChildren(renderTestStep({
      name: "Speichern",
      success: false,
      message: error.message,
      detail: null
    }));
  } finally {
    elements.saveLdapSettingsButton.disabled = false;
    elements.saveLdapSettingsButton.textContent = "Einstellungen speichern";
  }
}

function readLdapSettingsForm() {
  const password = elements.ldapBindPassword.value;
  return {
    server: elements.ldapServer.value.trim(),
    port: Number.parseInt(elements.ldapPort.value, 10) || 636,
    useSsl: elements.ldapUseSsl.checked,
    useStartTls: elements.ldapUseStartTls.checked,
    verifyCertificate: elements.ldapVerifyCertificate.checked,
    searchBase: elements.ldapSearchBase.value.trim(),
    groupPattern: elements.ldapGroupPattern.value.trim(),
    bindDn: elements.ldapBindDn.value.trim(),
    bindPassword: password.length > 0 ? password : null,
    clearBindPassword: elements.ldapClearPassword.checked,
    usePaging: elements.ldapUsePaging.checked
  };
}

function keepSingleTlsMode(event) {
  if (!event.target.checked) {
    return;
  }

  if (event.target === elements.ldapUseSsl) {
    elements.ldapUseStartTls.checked = false;
    elements.ldapPort.value = "636";
    return;
  }

  elements.ldapUseSsl.checked = false;
  elements.ldapPort.value = "389";
}

function normalizeTlsPort() {
  const port = Number.parseInt(elements.ldapPort.value, 10);
  if (elements.ldapUseSsl.checked && (Number.isNaN(port) || port === 389)) {
    elements.ldapPort.value = "636";
    return;
  }

  if (elements.ldapUseStartTls.checked && (Number.isNaN(port) || port === 636)) {
    elements.ldapPort.value = "389";
  }
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
  elements.userA.value = "";
  elements.userB.value = "";

  if (!state.showingComparison) {
    return;
  }

  state.showingComparison = false;
  state.comparison = [];
  elements.filter.value = "";
  updateFilterClearButton();
  state.filtered = [...state.results];
  renderRows(state.filtered);
  updateSummary();
  updateButtons();
}

function renderRows(rows) {
  setHeaders(["GroupName", "GroupPath", "SamAccountName", "DisplayName", "Mail", "Enabled", "Department", "Title", "DistinguishedName"]);
  elements.tbody.replaceChildren(...rows.map(row => renderRow(columns, row)));
}

function renderComparisonRows(rows) {
  setHeaders(["Status", "GroupName", "User 1", "User 2"]);
  colorComparisonHeaders();
  elements.tbody.replaceChildren(...rows.map(renderComparisonRow));
}

function setHeaders(headers) {
  const headerRow = elements.table.querySelector("thead tr");
  headerRow.replaceChildren(...headers.map(header => {
    const th = document.createElement("th");
    th.textContent = header;
    return th;
  }));
}

function colorComparisonHeaders() {
  elements.table.querySelectorAll("thead th").forEach(th => {
    th.classList.toggle("comparison-header-user-a", th.textContent === "User 1");
    th.classList.toggle("comparison-header-user-b", th.textContent === "User 2");
  });
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

function renderComparisonRow(row) {
  const tr = document.createElement("tr");
  tr.classList.add("comparison-row", comparisonStatusClass(row.status));
  tr.replaceChildren(...comparisonColumns.map(column => {
    const td = document.createElement("td");
    const value = row[column];
    td.textContent = value ?? "";
    td.title = td.textContent;

    if (column === "status") {
      td.classList.add("comparison-status");
    }

    return td;
  }));
  return tr;
}

function comparisonStatusClass(status) {
  switch (status) {
    case "Beide":
      return "comparison-status-both";
    case "Nur User 1":
      return "comparison-status-user-a";
    case "Nur User 2":
      return "comparison-status-user-b";
    default:
      return "comparison-status-neutral";
  }
}

async function copyGroups() {
  const groupNames = visibleGroupNames();
  if (groupNames.length === 0) {
    showToast("Keine Gruppen zum Kopieren sichtbar.");
    return;
  }

  const text = groupNames.join("\n");
  const copied = await copyText(text);
  if (copied) {
    showToast(`${groupNames.length} Gruppen kopiert.`);
    return;
  }

  openCopyGroupsDialog(text);
  showToast("Automatisches Kopieren blockiert. Gruppenliste wurde geöffnet.");
}

function openCopyGroupsDialog(text) {
  elements.copyGroupsText.value = text;
  if (typeof elements.copyGroupsDialog.showModal === "function") {
    elements.copyGroupsDialog.showModal();
  } else {
    elements.copyGroupsDialog.setAttribute("open", "");
  }

  elements.copyGroupsText.focus();
  elements.copyGroupsText.select();
}

async function copyText(text) {
  if (copyTextWithSelection(text)) {
    return true;
  }

  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Fall back for HTTP deployments where clipboard permission is unavailable.
    }
  }

  return false;
}

function copyTextWithSelection(text) {
  const textArea = document.createElement("textarea");
  textArea.value = text;
  textArea.setAttribute("readonly", "");
  textArea.style.position = "fixed";
  textArea.style.left = "-9999px";
  textArea.style.top = "0";
  document.body.append(textArea);
  textArea.focus();
  textArea.select();
  textArea.setSelectionRange(0, text.length);

  try {
    if (!document.execCommand("copy")) {
      return false;
    }
    return true;
  } catch {
    return false;
  } finally {
    textArea.remove();
  }
}

function exportCsv() {
  const activeColumns = state.showingComparison ? comparisonColumns : columns;
  const header = state.showingComparison
    ? ["Status", "GroupName", "UserA", "UserB"]
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
  if (!state.showingComparison && elements.filter.value.trim().length === 0 && state.groupNames.length > 0) {
    return [...state.groupNames];
  }

  return [...new Set(state.filtered.map(row => row.groupName).filter(Boolean))]
    .sort((a, b) => a.localeCompare(b));
}

function updateSummary() {
  setSummary(`${state.results.length} Zeilen · ${state.groupCount} Gruppen · ${state.userCount} Benutzer`);
}

function updateFilterSummary(filter) {
  if (!filter) {
    if (state.showingComparison) {
      setSummary(`${state.comparison.length} Vergleichszeilen`);
    } else {
      updateSummary();
    }
    return;
  }

  if (state.showingComparison) {
    setSummary(`${state.filtered.length} von ${state.comparison.length} Vergleichszeilen gefiltert`);
    return;
  }

  const visibleGroupCount = visibleGroupNames().length;
  setSummary(`${state.filtered.length} von ${state.results.length} Zeilen gefiltert · ${visibleGroupCount} Gruppen sichtbar`);
}

function setSummary(text, error = false) {
  elements.summary.textContent = text;
  elements.summary.classList.toggle("status-error", error);
}

function updateButtons() {
  const hasRows = state.filtered.length > 0;
  const hasResults = state.results.length > 0;
  const hasGroupsToCopy = visibleGroupNames().length > 0;
  elements.copyGroups.disabled = !hasGroupsToCopy;
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
