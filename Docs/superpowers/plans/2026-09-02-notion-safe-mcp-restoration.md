# Notion Safe MCP Restoration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild and register the historical five-tool `notion-safe-mcp` server with unrestricted reads and deny-by-default allowlisted writes.

**Architecture:** A Node.js ESM STDIO server delegates Notion REST calls to a small client and delegates every mutation decision to an independently tested policy module. Runtime state and the API token live outside Git at `C:\notion_custom\notion-safe-mcp`; Codex and Antigravity point to that one installation.

**Tech Stack:** Node.js 24, `@modelcontextprotocol/server` v2, Zod v4, native `fetch`, native `node:test`, Notion REST API `2026-03-11`.

**Spec:** `Docs/superpowers/specs/2026-09-02-notion-safe-mcp-restoration-design.md`

## Global Constraints

- Keep the runtime at `C:\notion_custom\notion-safe-mcp`.
- Use exactly one secret environment variable named `NOTION_API_TOKEN`.
- Never print, commit, snapshot, or return the token.
- Every mutation must pass the local allowlist before an HTTP mutation is attempted.
- Never expose delete/trash operations or set `allow_deleting_content`.
- Preserve the five historical tool names: `search`, `fetch`, `list_editable_targets`, `create_page`, and `update_page`.
- Write MCP protocol traffic only to stdout and diagnostics only to stderr.

---

### Task 1: Policy and persistent created-page registry

**Files:**
- Create: `%TEMP%/notion-safe-mcp-rebuild/package.json`
- Create: `%TEMP%/notion-safe-mcp-rebuild/config/write-policy.json`
- Create: `%TEMP%/notion-safe-mcp-rebuild/data/created-pages.json`
- Create: `%TEMP%/notion-safe-mcp-rebuild/src/id.js`
- Create: `%TEMP%/notion-safe-mcp-rebuild/src/created-page-store.js`
- Create: `%TEMP%/notion-safe-mcp-rebuild/src/write-policy.js`
- Test: `%TEMP%/notion-safe-mcp-rebuild/test/write-policy.test.js`

**Interfaces:**
- Produces: `normalizeId(value): string`, `CreatedPageStore`, and `WritePolicy`.
- `WritePolicy.canCreateUnder(parentId): boolean` permits only configured parent IDs.
- `WritePolicy.canUpdate(pageId): Promise<{allowed:boolean, reason:string}>` permits direct, created, and verified descendant pages.

- [ ] **Step 1: Write policy tests**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeId } from '../src/id.js';
import { WritePolicy } from '../src/write-policy.js';

test('normalizes dashed Notion UUIDs', () => {
  assert.equal(normalizeId('3363C0EF-49EC-80CF-94F5-FA298388545C'), '3363c0ef49ec80cf94f5fa298388545c');
});

test('allows only configured create parents', () => {
  const policy = new WritePolicy({ creatableParentIds: ['36c512a5-9d5c-4d83-a1c0-0762131981ef'] });
  assert.equal(policy.canCreateUnder('36c512a59d5c4d83a1c00762131981ef'), true);
  assert.equal(policy.canCreateUnder('00000000-0000-0000-0000-000000000000'), false);
});

test('denies update when ancestry lookup fails', async () => {
  const policy = new WritePolicy({
    descendantRootIds: ['36c512a5-9d5c-4d83-a1c0-0762131981ef'],
    resolveParentId: async () => { throw new Error('network'); },
  });
  assert.equal((await policy.canUpdate('11111111-1111-1111-1111-111111111111')).allowed, false);
});
```

- [ ] **Step 2: Run the tests and confirm they fail because modules do not exist**

Run: `node --test test/write-policy.test.js`

Expected: non-zero exit with `ERR_MODULE_NOT_FOUND`.

- [ ] **Step 3: Implement normalized IDs, atomic JSON persistence, and deny-by-default policy**

```js
export function normalizeId(value) {
  const normalized = String(value ?? '').replaceAll('-', '').toLowerCase();
  if (!/^[0-9a-f]{32}$/.test(normalized)) throw new Error('Invalid Notion ID');
  return normalized;
}
```

`CreatedPageStore.add(id)` writes JSON to a sibling temporary file and renames it over `data/created-pages.json`. `WritePolicy` checks direct IDs first, then stored IDs, then follows at most 32 parent links while rejecting cycles and lookup errors.

- [ ] **Step 4: Run all policy tests**

Run: `node --test test/write-policy.test.js`

Expected: all tests pass.

- [ ] **Step 5: Commit repository documentation only**

Runtime source remains outside the Unity repository, so no runtime commit occurs in this task.

### Task 2: Notion REST client

**Files:**
- Create: `%TEMP%/notion-safe-mcp-rebuild/src/notion-client.js`
- Test: `%TEMP%/notion-safe-mcp-rebuild/test/notion-client.test.js`

**Interfaces:**
- Consumes: a token and injectable `fetchImpl`.
- Produces: `NotionClient.search`, `retrievePage`, `retrieveMarkdown`, `createPage`, `replaceMarkdown`, and `retrieveParentId`.

- [ ] **Step 1: Write request and sanitization tests with mocked fetch**

```js
test('sends the 2026-03-11 API version without exposing the token', async () => {
  const calls = [];
  const client = new NotionClient('secret-test-token', async (url, init) => {
    calls.push({ url, init });
    return new Response(JSON.stringify({ results: [], has_more: false, next_cursor: null }), { status: 200 });
  });
  await client.search({ query: 'MCP' });
  assert.equal(calls[0].init.headers['Notion-Version'], '2026-03-11');
  assert.doesNotMatch(JSON.stringify(await client.search({ query: '' })), /secret-test-token/);
});

test('sanitizes API errors', async () => {
  const client = new NotionClient('secret-test-token', async () =>
    new Response(JSON.stringify({ code: 'unauthorized', message: 'bad token' }), { status: 401 }));
  await assert.rejects(client.search({}), error =>
    error.message.includes('unauthorized') && !error.message.includes('secret-test-token'));
});
```

- [ ] **Step 2: Run the client tests and confirm failure**

Run: `node --test test/notion-client.test.js`

Expected: non-zero exit with `ERR_MODULE_NOT_FOUND`.

- [ ] **Step 3: Implement the REST client**

Use `https://api.notion.com/v1` as a constant origin and construct only fixed endpoint shapes. `replaceMarkdown` sends:

```js
{
  type: 'replace_content',
  replace_content: { new_str: markdown }
}
```

`createPage` sends `{ parent: { page_id: parentId }, markdown }`. Error messages are limited to HTTP status, Notion code, and Notion message.

- [ ] **Step 4: Run all REST-client tests**

Run: `node --test test/notion-client.test.js`

Expected: all tests pass.

### Task 3: Five MCP tools and STDIO server

**Files:**
- Create: `%TEMP%/notion-safe-mcp-rebuild/src/config.js`
- Create: `%TEMP%/notion-safe-mcp-rebuild/src/tools.js`
- Create: `%TEMP%/notion-safe-mcp-rebuild/src/server.js`
- Create: `%TEMP%/notion-safe-mcp-rebuild/src/index.js`
- Test: `%TEMP%/notion-safe-mcp-rebuild/test/tools.test.js`
- Test: `%TEMP%/notion-safe-mcp-rebuild/test/stdio.test.js`

**Interfaces:**
- Consumes: `NotionClient`, `WritePolicy`, `CreatedPageStore`.
- Produces: `buildServer(dependencies): McpServer` and executable `src/index.js`.

- [ ] **Step 1: Write tool behavior tests**

Tests assert that all five names are registered, rejected writes do not invoke mocked mutation methods, successful creation records the returned page ID, and update uses Markdown replacement only after policy approval.

```js
test('rejects a create before calling Notion', async () => {
  let called = false;
  const tools = createToolHandlers({
    client: { createPage: async () => { called = true; } },
    policy: { canCreateUnder: () => false },
  });
  await assert.rejects(tools.create_page({
    parent_id: '00000000-0000-0000-0000-000000000000',
    markdown: '# blocked',
  }), /not allowlisted/i);
  assert.equal(called, false);
});
```

- [ ] **Step 2: Run tool tests and confirm failure**

Run: `node --test test/tools.test.js test/stdio.test.js`

Expected: non-zero exit because the server modules do not exist.

- [ ] **Step 3: Install the MCP server package and Zod**

Run from staging directory: `npm install @modelcontextprotocol/server@2 zod@4`

Expected: `package-lock.json` is generated and `npm audit` reports no unresolved high/critical runtime vulnerabilities.

- [ ] **Step 4: Implement handlers and server registration**

Register Zod schemas with required IDs and Markdown strings. Return every success as one JSON text content block. Mark `search`, `fetch`, and `list_editable_targets` read-only; mark `create_page` and `update_page` destructive/write operations for host approval behavior.

- [ ] **Step 5: Implement STDIO entry point**

```js
import { serveStdio } from '@modelcontextprotocol/server/stdio';
import { buildServer } from './server.js';

void serveStdio(() => buildServer());
console.error('notion-safe-mcp running on stdio');
```

- [ ] **Step 6: Run the full offline suite**

Run: `npm test`

Expected: all policy, client, tool, and STDIO tests pass without network access or real tokens.

### Task 4: Install runtime, migrate the token, and register clients

**Files:**
- Create: `C:/notion_custom/notion-safe-mcp/**` from the verified staging tree
- Create: `C:/notion_custom/notion-safe-mcp/.env`
- Modify: `C:/Users/user/.codex/config.toml`
- Modify: `C:/Users/user/.gemini/config/mcp_config.json`
- Preserve: timestamped backups beside both modified configuration files

**Interfaces:**
- Consumes: the existing `mcpServers.notion.env.NOTION_API_TOKEN` value without printing it.
- Produces: one shared installed STDIO server and two client registrations.

- [ ] **Step 1: Verify exact source and destination paths before copying**

Resolve and print only these non-secret paths:

```text
%TEMP%\notion-safe-mcp-rebuild
C:\notion_custom\notion-safe-mcp
C:\Users\user\.codex\config.toml
C:\Users\user\.gemini\config\mcp_config.json
```

- [ ] **Step 2: Copy the verified runtime and dependencies**

Create `C:\notion_custom` if absent and copy the staging directory to `C:\notion_custom\notion-safe-mcp`. Do not delete unrelated paths.

- [ ] **Step 3: Import the existing token without emitting it**

Read the token in-process from `C:\Users\user\.gemini\config\mcp_config.json`, validate that it is a non-empty string, and atomically write:

```text
NOTION_API_TOKEN=<existing value>
```

to `.env`. Command output reports only success and the environment-variable name.

- [ ] **Step 4: Back up and update client configurations**

Add `[mcp_servers.notion-safe-mcp]` to Codex with `command = "node"` and the two historical arguments. Set default tool approval to `writes`. In Antigravity, preserve `notion-safe-mcp` and set the unrestricted `notion.disabled` field to `true`.

- [ ] **Step 5: Verify configuration without printing credentials**

Parse both files and print server names, commands, enabled state, argument paths, and environment-variable names only. Confirm the destination source, `.env`, policy, and created-page registry exist.

### Task 5: Live smoke and write-boundary verification

**Files:**
- Modify only through the Notion API: one disposable child page under `36c512a5-9d5c-4d83-a1c0-0762131981ef`
- Modify: `C:/notion_custom/notion-safe-mcp/data/created-pages.json`

**Interfaces:**
- Consumes: the installed STDIO server.
- Produces: evidence that live reads work and local write enforcement cannot be bypassed through MCP tools.

- [ ] **Step 1: Start an MCP client test process and list tools**

Expected names: `search`, `fetch`, `list_editable_targets`, `create_page`, `update_page`.

- [ ] **Step 2: Run live search and fetch**

Search for `MCP`, fetch one returned page, and report only title, ID, URL, Markdown length, and truncation state.

- [ ] **Step 3: Prove denied writes do not reach Notion**

Call `create_page` and `update_page` with `00000000-0000-0000-0000-000000000000`. Both must return local allowlist errors rather than Notion HTTP errors.

- [ ] **Step 4: Create and update a disposable allowed page**

Create under the allowed parent with Markdown `# notion-safe-mcp 복구 테스트\n\n생성 테스트`, then replace it with `# notion-safe-mcp 복구 테스트\n\n생성 및 수정 테스트 완료`. Confirm its ID appears in `created-pages.json`.

- [ ] **Step 5: Final verification and restart handoff**

Run `npm test` in the installed directory, verify the two sanitized client registrations, and tell the user that Codex and Antigravity must restart before the restored tools appear in new sessions.
