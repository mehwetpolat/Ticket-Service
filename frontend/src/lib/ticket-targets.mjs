/**
 * Ticket hedeflerinin ortak tanımları. Hem sunucu hem tarayıcı tarafında
 * kullanılır; Node testleri de doğrudan bu dosyayı içe aktarabilir.
 */
export const TICKET_TARGETS = Object.freeze(["jira", "trello", "github", "asana", "clickup"]);

const definitions = {
  jira: {
    label: "Jira Cloud",
    shortLabel: "Jira",
    publishLabel: "Jira'da oluştur",
    destinationLabel: "Jira proje anahtarı",
    destinationPlaceholder: "IT",
    destinationHelp: "Jira'da projeyi açın; adres çubuğunda /projects/IT görünüyorsa anahtar IT'dir. Kayıt projenin backlog bölümüne düşer.",
    docsUrl: "https://id.atlassian.com/manage-profile/security/api-tokens",
    apiDocsUrl: "https://developer.atlassian.com/cloud/jira/platform/rest/v3/",
    pattern: /^[A-Z][A-Z0-9_]{1,29}$/,
    patternError: "Jira proje anahtarı 2-30 karakter olmalı, büyük harfle başlamalı ve yalnızca A-Z, 0-9, _ içermelidir (örn. IT, HELPDESK).",
    attachments: true,
  },
  trello: {
    label: "Trello",
    shortLabel: "Trello",
    publishLabel: "Trello'da oluştur",
    destinationLabel: "Trello liste ID",
    destinationPlaceholder: "5f1a2b3c4d5e6f7890abcdef",
    destinationHelp: "Kartın açılacağı listenin 24 karakterlik kimliği. Board adresinin sonuna .json ekleyip lists[].id alanından veya Trello API'sinden alabilirsiniz.",
    docsUrl: "https://trello.com/app-key",
    apiDocsUrl: "https://developer.atlassian.com/cloud/trello/rest/api-group-cards/#api-cards-post",
    pattern: /^[a-f0-9]{24}$/,
    patternError: "Trello liste ID'si 24 karakterlik hexadecimal olmalıdır (örn. 5f1a2b3c4d5e6f7890abcdef).",
    attachments: false,
  },
  github: {
    label: "GitHub Issues",
    shortLabel: "GitHub",
    publishLabel: "GitHub'da oluştur",
    destinationLabel: "GitHub deposu",
    destinationPlaceholder: "owner/repo",
    destinationHelp: "Issue'nun açılacağı depo owner/repo biçiminde yazılır. Örn. https://github.com/acme/destek için acme/destek. Depoda Issues sekmesi açık olmalıdır.",
    docsUrl: "https://github.com/settings/tokens?type=beta",
    apiDocsUrl: "https://docs.github.com/en/rest/issues/issues#create-an-issue",
    pattern: /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/,
    patternError: "GitHub deposu owner/repo biçiminde olmalıdır (örn. acme/destek).",
    attachments: false,
  },
  asana: {
    label: "Asana",
    shortLabel: "Asana",
    publishLabel: "Asana'da oluştur",
    destinationLabel: "Asana proje ID",
    destinationPlaceholder: "1201234567890123",
    destinationHelp: "Görevin ekleneceği Asana projesinin sayısal GID'si. Proje adresindeki /0/<proje-GID>/ bölümünden alınır.",
    docsUrl: "https://developers.asana.com/docs/authentication",
    apiDocsUrl: "https://developers.asana.com/reference/createtask",
    pattern: /^\d{5,}$/,
    patternError: "Asana proje ID'si sayısal olmalıdır (örn. 1201234567890123).",
    attachments: false,
  },
  clickup: {
    label: "ClickUp",
    shortLabel: "ClickUp",
    publishLabel: "ClickUp'ta oluştur",
    destinationLabel: "ClickUp liste ID",
    destinationPlaceholder: "901234567",
    destinationHelp: "Görevin açılacağı ClickUp listesinin kimliği. Liste adresindeki /li/<liste-ID> bölümünden alınır.",
    docsUrl: "https://app.clickup.com/settings/apps",
    apiDocsUrl: "https://developer.clickup.com/reference/createtask",
    pattern: /^\d{5,}$/,
    patternError: "ClickUp liste ID'si sayısal olmalıdır (örn. 901234567).",
    attachments: false,
  },
};

export const TICKET_TARGET_DEFINITIONS = Object.freeze(definitions);

/** @param {unknown} value */
export function isTicketTarget(value) {
  return typeof value === "string" && TICKET_TARGETS.includes(value);
}

/** @param {string} target */
export function targetDefinition(target) {
  return isTicketTarget(target) ? definitions[target] : definitions.jira;
}

/** @param {string} target */
export function targetLabel(target) {
  return targetDefinition(target).label;
}

/** @param {string} target */
export function targetShortLabel(target) {
  return targetDefinition(target).shortLabel;
}

/**
 * Kullanıcıdan gelen hedef kimliğini sağlayıcıya göre normalleştirir.
 * GitHub'da tam depo URL'si de kabul edilir.
 * @param {string} target
 * @param {string} value
 */
export function normalizeDestination(target, value) {
  let destination = String(value ?? "").trim();
  if (target === "github") {
    const repoUrl = destination.match(/^(?:https?:\/\/)?(?:www\.)?github\.com\/([^/\s]+)\/([^/\s#?]+)(?:[/?#].*)?$/i);
    if (repoUrl) destination = `${repoUrl[1]}/${repoUrl[2]}`;
    destination = destination.replace(/\.git$/i, "").replace(/\/+$/, "");
  } else if (target === "jira") {
    destination = destination.toUpperCase();
  } else if (target === "trello") {
    destination = destination.toLowerCase();
  }
  return destination;
}

/**
 * Hedef kimliğini doğrular; geçerliyse null, değilse kullanıcıya gösterilecek
 * Türkçe hata mesajını döndürür.
 * @param {string} target
 * @param {string} value
 */
export function destinationError(target, value) {
  const definition = targetDefinition(target);
  if (!isTicketTarget(target)) return "Desteklenmeyen ticket hedefi.";
  if (!value) return `${definition.destinationLabel} gerekli.`;
  const normalized = normalizeDestination(target, value);
  if (!definition.pattern.test(normalized)) return definition.patternError;
  return null;
}

/**
 * Sağlayıcı için zorunlu kimlik bilgilerinin tamamı girilmiş mi?
 * @param {string} target
 * @param {Record<string, string> | null | undefined} config
 */
export function providerConfigured(target, config) {
  if (!isTicketTarget(target) || !config) return false;
  if (target === "jira") return Boolean(config.baseUrl && config.email && config.token);
  if (target === "trello") return Boolean(config.key && config.token);
  return Boolean(config.token);
}
