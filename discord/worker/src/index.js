const DISCORD_API = "https://discord.com/api/v10";
const GITHUB_API = "https://api.github.com";
const MAX_PACKAGE_BYTES = 40_000;
const MAX_ADMIN_REQUEST_BYTES = 100_000;
const MAX_ADMIN_PHOTO_REQUEST_BYTES = 4_200_000;
const MAX_WORKSHOP_PHOTO_BYTES = 3 * 1_048_576;
const DISCORD_EPHEMERAL = 64;

class PayloadTooLargeError extends Error {}
class CatalogEntryNotFoundError extends Error {}

const COMMANDS = [
  {
    name: "submit",
    description: "Отправить набор биндов на модерацию мастерской",
    options: [
      {
        name: "name",
        description: "Название набора",
        type: 3,
        required: true,
        max_length: 60,
      },
      {
        name: "description",
        description: "Краткое описание набора",
        type: 3,
        required: true,
        max_length: 250,
      },
      {
        name: "type",
        description: "Что содержит JSON-файл",
        type: 3,
        required: true,
        choices: [
          { name: "Целый профиль", value: "profile" },
          { name: "Набор биндов для выбранной вкладки", value: "binds" },
        ],
      },
      {
        name: "package",
        description: "JSON-файл профиля или биндов",
        type: 11,
        required: true,
      },
    ],
  },
  {
    name: "approve",
    description: "Одобрить отправку мастерской и опубликовать её",
    options: [
      {
        name: "issue",
        description: "Номер заявки GitHub",
        type: 4,
        required: true,
        min_value: 1,
      },
    ],
  },
];

export default {
  async fetch(request, env, context) {
    const pathname = new URL(request.url).pathname;
    if (pathname.startsWith("/admin/")) {
      return handleAdminRequest(request, env, pathname);
    }

    if (request.method !== "POST" || pathname !== "/discord") {
      return new Response("Not found", { status: 404 });
    }

    const rawBody = await request.arrayBuffer();
    if (!(await verifyDiscordRequest(request, rawBody, env.DISCORD_PUBLIC_KEY))) {
      return new Response("Invalid request signature", { status: 401 });
    }

    let interaction;
    try {
      interaction = JSON.parse(new TextDecoder().decode(rawBody));
    } catch {
      return new Response("Invalid JSON", { status: 400 });
    }

    if (interaction.type === 1) {
      return Response.json({ type: 1 });
    }

    if (interaction.type !== 2) {
      return Response.json({
        type: 4,
        data: { content: "Этот тип взаимодействия не поддерживается.", flags: DISCORD_EPHEMERAL },
      });
    }

    if (interaction.data?.name === "approve" && !isAdministrator(interaction, env)) {
      return Response.json({
        type: 4,
        data: { content: "Одобрять публикации может только администратор мастерской.", flags: DISCORD_EPHEMERAL },
      });
    }

    context.waitUntil(processInteraction(interaction, env));
    return Response.json({
      type: 5,
      data: { flags: DISCORD_EPHEMERAL },
    });
  },
};

async function handleAdminRequest(request, env, pathname) {
  if (request.method !== "POST" ||
      ![
        "/admin/status",
        "/admin/publish",
        "/admin/publish-photo",
        "/admin/update-entry",
        "/admin/hide-entry",
      ].includes(pathname)) {
    return Response.json({ error: "Неизвестный запрос админки." }, { status: 404 });
  }

  if (!env.ADMIN_HWID_SHA256 || !/^[0-9a-f]{64}$/i.test(env.ADMIN_HWID_SHA256)) {
    return Response.json({ error: "Проверка HWID не настроена на Worker." }, { status: 503 });
  }

  const maxRequestBytes = pathname === "/admin/publish-photo"
    ? MAX_ADMIN_PHOTO_REQUEST_BYTES
    : MAX_ADMIN_REQUEST_BYTES;
  if (Number(request.headers.get("Content-Length") ?? 0) > maxRequestBytes) {
    return Response.json({ error: "Запрос админки слишком большой." }, { status: 413 });
  }

  let body;
  try {
    const bytes = await readBounded(request, maxRequestBytes);
    body = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
  } catch (error) {
    const status = error instanceof PayloadTooLargeError ? 413 : 400;
    return Response.json({ error: safeError(error) }, { status });
  }

  if (!isAuthorizedHardwareId(body?.hardwareId, env.ADMIN_HWID_SHA256)) {
    return Response.json({ error: "Доступ к админке с этого устройства запрещён." }, { status: 403 });
  }

  if (pathname === "/admin/status") {
    return Response.json({ authorized: true });
  }

  if (pathname === "/admin/publish-photo") {
    let submission;
    try {
      submission = validatePhotoSubmission(body);
    } catch (error) {
      const status = error instanceof PayloadTooLargeError ? 413 : 400;
      return Response.json({ error: safeError(error) }, { status });
    }

    try {
      const repository = getRepository(env);
      await publishPhotoSubmission(env, repository, submission);
      return Response.json({ message: `Фото-запись «${submission.name}» опубликована.` });
    } catch (error) {
      console.error("Workshop photo publish failed", error);
      return Response.json({ error: safeError(error) }, { status: 500 });
    }
  }

  if (pathname === "/admin/update-entry" || pathname === "/admin/hide-entry") {
    const id = typeof body.id === "string" ? body.id.trim() : "";
    if (!id || id.length > 120) {
      return Response.json({ error: "Укажи корректную запись каталога." }, { status: 400 });
    }

    if (pathname === "/admin/update-entry") {
      const name = typeof body.name === "string" ? body.name.trim() : "";
      const description = typeof body.description === "string" ? body.description.trim() : "";
      if (!name || name.length > 60 || !description || description.length > 250) {
        return Response.json(
          { error: "Название должно содержать до 60 символов, описание — до 250." },
          { status: 400 },
        );
      }
    }

    try {
      const repository = getRepository(env);
      const message = pathname === "/admin/update-entry"
        ? await updateCatalogEntry(env, repository, id, body.name.trim(), body.description.trim())
        : await hideCatalogEntry(env, repository, id);
      return Response.json({ message });
    } catch (error) {
      const status = error instanceof CatalogEntryNotFoundError ? 404 : 500;
      if (status === 500) {
        console.error("Workshop catalog management failed", pathname, error);
      }
      return Response.json({ error: safeError(error) }, { status });
    }
  }

  const name = typeof body.name === "string" ? body.name.trim() : "";
  const description = typeof body.description === "string" ? body.description.trim() : "";
  let packageData;
  try {
    if (!name || name.length > 60 || !description || description.length > 250) {
      throw new Error("Название должно содержать до 60 символов, описание — до 250.");
    }

    packageData = validatePackage("binds", body.package);
    if (new TextEncoder().encode(JSON.stringify(packageData)).byteLength > MAX_PACKAGE_BYTES) {
      throw new Error("Размер набора биндов не должен превышать 40 КБ.");
    }
  } catch (error) {
    return Response.json({ error: safeError(error) }, { status: 400 });
  }

  const id = crypto.randomUUID();
  const submission = {
    id,
    catalogId: `hwid-${id}`,
    name,
    description,
    type: "binds",
    package: packageData,
  };
  try {
    await publishSubmission(env, getRepository(env), submission, `hwid-${id}.json`);
    return Response.json({ message: `Набор «${name}» опубликован в мастерской.` });
  } catch (error) {
    console.error("Workshop admin publish failed", error);
    return Response.json({ error: safeError(error) }, { status: 500 });
  }
}

function validatePhotoSubmission(body) {
  const name = typeof body?.name === "string" ? body.name.trim() : "";
  const description = typeof body?.description === "string" ? body.description.trim() : "";
  const contentType = body?.contentType;
  const imageBase64 = body?.imageBase64;
  if (!name || name.length > 60 || !description || description.length > 250) {
    throw new Error("Название должно содержать до 60 символов, описание — до 250.");
  }

  if (!["image/jpeg", "image/png"].includes(contentType) ||
      typeof imageBase64 !== "string" ||
      !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(imageBase64)) {
    throw new Error("Пришли корректное фото в формате JPG или PNG.");
  }

  const imageBytes = base64ToBytes(imageBase64);
  if (imageBytes.length < 1 || imageBytes.length > MAX_WORKSHOP_PHOTO_BYTES) {
    throw new PayloadTooLargeError("Фото должно быть размером не более 3 МБ.");
  }

  const isJpeg = contentType === "image/jpeg" &&
    imageBytes.length >= 3 &&
    imageBytes[0] === 0xff &&
    imageBytes[1] === 0xd8 &&
    imageBytes[2] === 0xff;
  const isPng = contentType === "image/png" &&
    imageBytes.length >= 8 &&
    imageBytes[0] === 0x89 &&
    imageBytes[1] === 0x50 &&
    imageBytes[2] === 0x4e &&
    imageBytes[3] === 0x47 &&
    imageBytes[4] === 0x0d &&
    imageBytes[5] === 0x0a &&
    imageBytes[6] === 0x1a &&
    imageBytes[7] === 0x0a;
  if (!isJpeg && !isPng) {
    throw new Error("Содержимое файла не соответствует формату JPG или PNG.");
  }

  const extension = contentType === "image/jpeg" ? "jpg" : "png";
  return {
    id: crypto.randomUUID(),
    name,
    description,
    imageBase64,
    extension,
  };
}

function isAuthorizedHardwareId(hardwareId, expectedHash) {
  if (typeof hardwareId !== "string" || !/^[0-9a-f]{64}$/i.test(hardwareId)) {
    return false;
  }

  const expected = expectedHash.toLowerCase();
  const actual = hardwareId.toLowerCase();
  let difference = 0;
  for (let index = 0; index < expectedHash.length; index += 1) {
    difference |= expected.charCodeAt(index) ^ actual.charCodeAt(index);
  }
  return difference === 0;
}

async function verifyDiscordRequest(request, body, publicKeyHex) {
  const signatureHex = request.headers.get("X-Signature-Ed25519");
  const timestamp = request.headers.get("X-Signature-Timestamp");
  const requestTime = Number(timestamp);
  const currentTime = Math.floor(Date.now() / 1000);
  if (!signatureHex || !timestamp || !/^[0-9a-f]{128}$/i.test(signatureHex) ||
      !/^[0-9a-f]{64}$/i.test(publicKeyHex ?? "") ||
      !Number.isSafeInteger(requestTime) || Math.abs(currentTime - requestTime) > 300) {
    return false;
  }

  try {
    const key = await crypto.subtle.importKey(
      "raw",
      hexToBytes(publicKeyHex),
      { name: "Ed25519" },
      false,
      ["verify"],
    );
    const signedBody = new Uint8Array(
      new TextEncoder().encode(timestamp).length + body.byteLength,
    );
    signedBody.set(new TextEncoder().encode(timestamp));
    signedBody.set(new Uint8Array(body), new TextEncoder().encode(timestamp).length);

    return await crypto.subtle.verify(
      { name: "Ed25519" },
      key,
      hexToBytes(signatureHex),
      signedBody,
    );
  } catch {
    return false;
  }
}

function hexToBytes(hex) {
  const bytes = new Uint8Array(hex.length / 2);
  for (let index = 0; index < bytes.length; index += 1) {
    bytes[index] = Number.parseInt(hex.slice(index * 2, index * 2 + 2), 16);
  }
  return bytes;
}

function isAdministrator(interaction, env) {
  const userId = interaction.member?.user?.id ?? interaction.user?.id;
  return Boolean(env.ADMIN_DISCORD_USER_ID) && userId === env.ADMIN_DISCORD_USER_ID;
}

function getOption(interaction, name) {
  return interaction.data?.options?.find((option) => option.name === name)?.value;
}

async function processInteraction(interaction, env) {
  try {
    let message;
    if (interaction.data.name === "submit") {
      message = await submitPackage(interaction, env);
    } else if (interaction.data.name === "approve") {
      message = await approvePackage(interaction, env);
    } else {
      message = "Неизвестная команда мастерской.";
    }
    await sendFollowup(interaction, message);
  } catch (error) {
    console.error("Workshop command failed", error);
    await sendFollowup(interaction, `Не удалось выполнить команду: ${safeError(error)}`);
  }
}

async function submitPackage(interaction, env) {
  const name = getOption(interaction, "name")?.trim();
  const description = getOption(interaction, "description")?.trim();
  const type = getOption(interaction, "type");
  const attachmentId = getOption(interaction, "package");
  const attachment = interaction.data?.resolved?.attachments?.[attachmentId];

  if (!name || name.length > 60 || !description || description.length > 250 ||
      !["profile", "binds"].includes(type) || !attachment) {
    throw new Error("Проверь название, описание, тип набора и прикреплённый JSON.");
  }

  if (attachment.size > MAX_PACKAGE_BYTES || !attachment.filename?.toLowerCase().endsWith(".json")) {
    throw new Error("Нужен JSON-файл размером не более 40 КБ.");
  }

  const packageResponse = await fetch(attachment.url);
  if (!packageResponse.ok) {
    throw new Error("Не удалось загрузить JSON-вложение Discord.");
  }
  const packageBytes = await readBounded(packageResponse, MAX_PACKAGE_BYTES);
  const packageText = new TextDecoder("utf-8", { fatal: true }).decode(packageBytes);
  const packageData = JSON.parse(packageText);
  const normalizedPackage = validatePackage(type, packageData);
  const submission = {
    id: crypto.randomUUID(),
    name,
    description,
    type,
    package: normalizedPackage,
    submitter: interaction.member?.user?.id ?? interaction.user?.id,
  };
  const encoded = bytesToBase64(new TextEncoder().encode(JSON.stringify(submission)));
  const repository = getRepository(env);
  const issue = await githubRequest(
    env,
    "POST",
    `/repos/${repository}/issues`,
    {
      title: `[Workshop] ${name}`,
      body: `Заявка от Discord-пользователя ${submission.submitter}\n\n${description}\n\n<!-- LUM_WORKSHOP_PENDING_V1:${encoded} -->`,
    },
  );

  return `Заявка **${name}** отправлена на модерацию: ${issue.html_url}`;
}

async function readBounded(response, limit) {
  if (Number(response.headers.get("Content-Length") ?? 0) > limit) {
    throw new PayloadTooLargeError(`JSON-файл больше ${limit / 1000} КБ.`);
  }
  const reader = response.body.getReader();
  const chunks = [];
  let total = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.byteLength;
    if (total > limit) {
      await reader.cancel();
      throw new PayloadTooLargeError(`JSON-файл больше ${limit / 1000} КБ.`);
    }
    chunks.push(value);
  }

  const bytes = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return bytes;
}

function validatePackage(type, data) {
  if (type === "profile") {
    if (!Array.isArray(data?.tabs) || data.tabs.length < 1 || data.tabs.length > 20) {
      throw new Error("Профиль должен содержать от 1 до 20 вкладок.");
    }
    return { tabs: data.tabs.map((tab) => validateTab(tab)) };
  }

  if (!Array.isArray(data?.binds) || data.binds.length < 1 || data.binds.length > 100) {
    throw new Error("Набор должен содержать от 1 до 100 биндов.");
  }
  return { binds: data.binds.map((bind) => validateBind(bind)) };
}

function validateTab(tab) {
  if (typeof tab?.name !== "string" || !tab.name.trim() || tab.name.length > 60 ||
      !Array.isArray(tab.binds) || tab.binds.length > 100) {
    throw new Error("Вкладка должна иметь название и не более 100 биндов.");
  }
  return { name: tab.name.trim(), binds: tab.binds.map((bind) => validateBind(bind)) };
}

function validateBind(bind) {
  if (typeof bind?.name !== "string" || !bind.name.trim() || bind.name.length > 80 ||
      typeof bind.hotkey !== "string" || bind.hotkey.length > 40 ||
      !Array.isArray(bind.steps) || bind.steps.length < 1 || bind.steps.length > 25 ||
      bind.steps.some((step) => typeof step !== "string" || step.length > 1000)) {
    throw new Error("Бинд должен иметь название, сочетание клавиш и от 1 до 25 текстовых шагов.");
  }
  return {
    name: bind.name.trim(),
    hotkey: bind.hotkey,
    enabled: bind.enabled !== false,
    steps: bind.steps,
  };
}

async function approvePackage(interaction, env) {
  const issueNumber = getOption(interaction, "issue");
  if (!Number.isSafeInteger(issueNumber) || issueNumber < 1) {
    throw new Error("Укажи корректный номер заявки GitHub.");
  }

  const repository = getRepository(env);
  const issue = await githubRequest(env, "GET", `/repos/${repository}/issues/${issueNumber}`);
  if (issue.state !== "open" || !issue.title.startsWith("[Workshop]")) {
    throw new Error("Открытая заявка мастерской с таким номером не найдена.");
  }

  const marker = issue.body?.match(/<!-- LUM_WORKSHOP_PENDING_V1:([A-Za-z0-9+/=]+) -->/);
  if (!marker) {
    throw new Error("В заявке нет корректного JSON-пакета мастерской.");
  }

  const submission = JSON.parse(new TextDecoder().decode(base64ToBytes(marker[1])));
  if (!submission || typeof submission.name !== "string" ||
      typeof submission.description !== "string" ||
      !["profile", "binds"].includes(submission.type) ||
      typeof submission.id !== "string" ||
      !submission.package) {
    throw new Error("Заявка содержит некорректные данные.");
  }
  submission.package = validatePackage(submission.type, submission.package);
  const packageFile = `discord-${issueNumber}.json`;
  await publishSubmission(env, repository, submission, packageFile);
  await githubRequest(env, "POST", `/repos/${repository}/issues/${issueNumber}/comments`, {
    body: `Опубликовано в мастерской: **${submission.name}** (${submission.type}).`,
  });
  await githubRequest(env, "PATCH", `/repos/${repository}/issues/${issueNumber}`, { state: "closed" });
  return `Заявка **${submission.name}** одобрена и опубликована. Каталог можно обновить в приложении.`;
}

async function publishSubmission(env, repository, submission, packageFile) {
  const item = {
    id: submission.catalogId ?? `discord-${submission.id}`,
    name: submission.name,
    description: submission.description,
    type: submission.type,
    file: packageFile,
  };
  const packageBlob = await githubRequest(env, "POST", `/repos/${repository}/git/blobs`, {
    content: JSON.stringify(submission.package, null, 2),
    encoding: "utf-8",
  });
  await publishCatalogEntry(
    env,
    repository,
    item,
    [{ path: `workshop/packs/${packageFile}`, mode: "100644", type: "blob", sha: packageBlob.sha }],
    `Publish workshop package: ${submission.name}`,
  );
}

async function publishPhotoSubmission(env, repository, submission) {
  const imageFile = `photo-${submission.id}.${submission.extension}`;
  const imageBlob = await githubRequest(env, "POST", `/repos/${repository}/git/blobs`, {
    content: submission.imageBase64,
    encoding: "base64",
  });
  const item = {
    id: `admin-photo-${submission.id}`,
    name: submission.name,
    description: submission.description,
    type: "photo",
    image: imageFile,
  };
  await publishCatalogEntry(
    env,
    repository,
    item,
    [{ path: `workshop/images/${imageFile}`, mode: "100644", type: "blob", sha: imageBlob.sha }],
    `Publish workshop photo: ${submission.name}`,
  );
}

async function publishCatalogEntry(env, repository, item, contentTreeEntries, commitMessage) {
  const ref = await githubRequest(env, "GET", `/repos/${repository}/git/ref/heads/main`);
  const parentSha = ref.object.sha;
  const catalogResponse = await githubRequest(
    env,
    "GET",
    `/repos/${repository}/contents/workshop/catalog.json?ref=${parentSha}`,
    undefined,
  );
  const catalogBytes = base64ToBytes(catalogResponse.content.replace(/\s/g, ""));
  const catalog = JSON.parse(new TextDecoder().decode(catalogBytes));
  if (!Array.isArray(catalog)) {
    throw new Error("Файл каталога мастерской имеет некорректный формат.");
  }
  if (catalog.some((entry) => entry.id === item.id)) {
    throw new Error("Запись с таким идентификатором уже существует; существующие записи не изменяются.");
  }

  const nextCatalog = [...catalog, item];
  const catalogBlob = await githubRequest(env, "POST", `/repos/${repository}/git/blobs`, {
    content: `${JSON.stringify(nextCatalog, null, 2)}\n`,
    encoding: "utf-8",
  });
  await commitCatalogBlob(env, repository, parentSha, catalogBlob.sha, contentTreeEntries, commitMessage);
}

async function updateCatalogEntry(env, repository, id, name, description) {
  const { parentSha, catalog } = await loadCatalogAtHead(env, repository);
  const index = catalog.findIndex((entry) => entry.id === id);
  if (index < 0) {
    throw new CatalogEntryNotFoundError("Запись не найдена в каталоге. Обнови список и попробуй ещё раз.");
  }

  const updatedCatalog = catalog.map((entry, entryIndex) =>
    entryIndex === index ? { ...entry, name, description } : entry);
  const catalogBlob = await githubRequest(env, "POST", `/repos/${repository}/git/blobs`, {
    content: `${JSON.stringify(updatedCatalog, null, 2)}\n`,
    encoding: "utf-8",
  });
  await commitCatalogBlob(
    env,
    repository,
    parentSha,
    catalogBlob.sha,
    [],
    `Update workshop entry: ${name}`,
  );
  return `Запись «${name}» обновлена.`;
}

async function hideCatalogEntry(env, repository, id) {
  const { parentSha, catalog } = await loadCatalogAtHead(env, repository);
  const item = catalog.find((entry) => entry.id === id);
  if (!item) {
    throw new CatalogEntryNotFoundError("Запись не найдена в каталоге. Обнови список и попробуй ещё раз.");
  }

  const nextCatalog = catalog.filter((entry) => entry.id !== id);
  const catalogBlob = await githubRequest(env, "POST", `/repos/${repository}/git/blobs`, {
    content: `${JSON.stringify(nextCatalog, null, 2)}\n`,
    encoding: "utf-8",
  });
  await commitCatalogBlob(
    env,
    repository,
    parentSha,
    catalogBlob.sha,
    [],
    `Hide workshop entry: ${item.name}`,
  );
  return `Запись «${item.name}» убрана из каталога. Файлы сохранены.`;
}

async function loadCatalogAtHead(env, repository) {
  const ref = await githubRequest(env, "GET", `/repos/${repository}/git/ref/heads/main`);
  const parentSha = ref.object.sha;
  const catalogResponse = await githubRequest(
    env,
    "GET",
    `/repos/${repository}/contents/workshop/catalog.json?ref=${parentSha}`,
    undefined,
  );
  const catalogBytes = base64ToBytes(catalogResponse.content.replace(/\s/g, ""));
  const catalog = JSON.parse(new TextDecoder().decode(catalogBytes));
  if (!Array.isArray(catalog)) {
    throw new Error("Файл каталога мастерской имеет некорректный формат.");
  }
  return { parentSha, catalog };
}

async function commitCatalogBlob(env, repository, parentSha, catalogBlobSha, contentTreeEntries, commitMessage) {
  const tree = await githubRequest(env, "POST", `/repos/${repository}/git/trees`, {
    base_tree: parentSha,
    tree: [
      ...contentTreeEntries,
      { path: "workshop/catalog.json", mode: "100644", type: "blob", sha: catalogBlobSha },
    ],
  });
  const commit = await githubRequest(env, "POST", `/repos/${repository}/git/commits`, {
    message: commitMessage,
    tree: tree.sha,
    parents: [parentSha],
  });
  await githubRequest(env, "PATCH", `/repos/${repository}/git/refs/heads/main`, {
    sha: commit.sha,
    force: false,
  });
}

async function githubRequest(env, method, path, body) {
  if (!env.GITHUB_TOKEN) {
    throw new Error("Интеграция GitHub не настроена.");
  }
  const response = await fetch(`${GITHUB_API}${path}`, {
    method,
    headers: {
      Accept: "application/vnd.github+json",
      Authorization: `Bearer ${env.GITHUB_TOKEN}`,
      "X-GitHub-Api-Version": "2022-11-28",
      "User-Agent": "Lum-Binder-Workshop",
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const responseText = await response.text();
  if (!response.ok) {
    console.error("GitHub API request failed", method, path, response.status, responseText.slice(0, 500));
    throw new Error(`GitHub API вернул ошибку ${response.status}.`);
  }
  return responseText ? JSON.parse(responseText) : {};
}

async function sendFollowup(interaction, content) {
  const response = await fetch(
    `${DISCORD_API}/webhooks/${interaction.application_id}/${interaction.token}`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content, allowed_mentions: { parse: [] } }),
    },
  );
  if (!response.ok) {
    console.error("Discord follow-up failed", response.status, (await response.text()).slice(0, 500));
  }
}

function getRepository(env) {
  const repository = env.GITHUB_REPOSITORY ?? "";
  if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository)) {
    throw new Error("Репозиторий мастерской не настроен.");
  }
  return repository;
}

function bytesToBase64(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

function base64ToBytes(base64) {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}

function safeError(error) {
  return error instanceof Error ? error.message.slice(0, 300) : "Неизвестная ошибка.";
}
