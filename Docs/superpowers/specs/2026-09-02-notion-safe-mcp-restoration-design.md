# Notion Safe MCP Restoration Design

## Goal

Restore the deleted `notion-safe-mcp` local Node.js server so Codex and Antigravity can search and read all Notion content shared with one integration while writes remain restricted by a server-side allowlist.

## Existing Evidence

- The former server ran from `C:\notion_custom\notion-safe-mcp\src\index.js` over STDIO.
- Historical clients exposed `search`, `fetch`, `list_editable_targets`, `create_page`, and `update_page`.
- Historical calls prove that search, fetch, and allowlist rejection worked.
- The original source and `.env` are gone. This is a behavior-compatible reimplementation, not a byte-for-byte restoration.
- The existing integration uses one token for both reads and writes.

## Selected Approach

Build a focused local MCP server using Node.js and the official MCP SDK. Use the Notion REST API directly with API version `2026-03-11` so the server can use Notion's enhanced Markdown endpoints without block conversion code.

The same `NOTION_API_TOKEN` authenticates reads and writes. The server independently enforces the write policy before making any mutating HTTP request. Token possession alone must never bypass the allowlist.

## Installation Location

The runtime is restored at the historical location:

```text
C:\notion_custom\notion-safe-mcp
```

Runtime files are not committed to the Unity repository. The design and implementation plan are stored in the repository for auditability.

## Components

### `src/config.js`

- Reads `NOTION_API_TOKEN` from the environment.
- Loads immutable allowlist IDs from `config/write-policy.json`.
- Normalizes UUIDs by removing hyphens and lowercasing before comparison.
- Fails startup without printing the token when configuration is invalid.

### `src/write-policy.js`

- Permits direct updates to explicitly editable page IDs.
- Permits page creation only beneath explicitly creatable parent IDs.
- Persists IDs of pages created through this server in `data/created-pages.json`.
- Permits updates to pages recorded as server-created.
- Permits updates to descendants of configured parent IDs only after resolving their ancestor chain through the Notion API.
- Denies by default on malformed IDs, lookup failures, cycles, or excessive ancestry depth.

### `src/notion-client.js`

- Sends requests only to `https://api.notion.com/v1`.
- Uses `Authorization: Bearer ...`, `Content-Type: application/json`, and `Notion-Version: 2026-03-11`.
- Converts non-success responses into sanitized errors containing status, Notion error code, and message but no credentials or request headers.
- Implements search, page metadata retrieval, Markdown retrieval, page creation, Markdown replacement, and ancestor lookup.

### `src/tools.js`

Defines five MCP tools:

1. `search(query?, start_cursor?, page_size?)`
   - Searches objects visible to the integration.
   - Returns compact page/data-source identifiers, titles, URLs, pagination metadata, and no raw Notion payload.
2. `fetch(page_id, include_transcript?)`
   - Returns page title, URL, Markdown, truncation state, unknown block IDs, and basic metadata.
3. `list_editable_targets()`
   - Returns the effective direct-update, creatable-parent, descendant, and server-created ID lists.
4. `create_page(parent_id, markdown)`
   - Checks the parent allowlist before `POST /v1/pages`.
   - Uses the first H1 as the title when present, matching Notion Markdown API behavior.
   - Records the created page ID only after a successful response.
5. `update_page(page_id, markdown)`
   - Checks direct, created-page, or allowed-descendant policy.
   - Replaces page Markdown using `replace_content`.
   - Never sets `allow_deleting_content`, preserving Notion's child-page/database protection.

### `src/index.js`

- Registers tools and starts an MCP STDIO transport.
- Writes diagnostics only to stderr so stdout remains valid MCP traffic.

## Initial Write Policy

Directly editable page IDs:

```text
3363c0ef-49ec-80cf-94f5-fa298388545c
c29d3558-c3b3-4bfd-866c-8276fa9ecdce
2613c0ef-49ec-805c-8bcf-d6247da5ef03
2613c0ef-49ec-80b9-a0d3-e478095f2398
```

Creatable parent and editable-descendant root:

```text
36c512a5-9d5c-4d83-a1c0-0762131981ef
```

Historically server-created page to retain:

```text
3363c0ef-49ec-8163-889b-f9d58b3780d2
```

## Credentials

- `.env` contains only `NOTION_API_TOKEN=...` and is ignored by Git.
- The token is sourced from the currently configured Notion integration only during local installation.
- Tests use fake tokens and mocked HTTP; live credentials never enter snapshots, fixtures, logs, command output, or repository files.
- Existing plaintext copies in legacy Antigravity configuration are not deleted automatically. Credential cleanup and rotation are reported separately.

## Client Registration

### Codex

Register one STDIO server named `notion-safe-mcp` in `C:\Users\user\.codex\config.toml` with:

- command: `node`
- arguments: `--env-file=C:\notion_custom\notion-safe-mcp\.env`, `C:\notion_custom\notion-safe-mcp\src\index.js`
- write-tool approval prompts enabled for `create_page` and `update_page`

Codex clients share host MCP configuration but require a restart before a newly registered server becomes available in a session.

### Antigravity

Keep the existing `notion-safe-mcp` entry pointed at the restored files. Disable, but do not delete, the separate unrestricted `notion` entry to avoid ambiguous duplicate tools.

## Error Handling

- Invalid arguments produce MCP validation errors without calling Notion.
- Policy rejection names the denied page or parent ID and does not make a mutating request.
- Notion authentication and permission errors are reported distinctly.
- Created-page persistence uses atomic replacement to avoid corrupting the registry.
- A failed creation never adds an ID to the registry.
- Descendant resolution is bounded and deny-by-default.

## Verification

1. Unit-test ID normalization and every allow/deny policy branch.
2. Unit-test sanitized Notion errors and compact response mapping with mocked HTTP.
3. Integration-test MCP initialization and all five tool schemas over STDIO.
4. Run a live read-only smoke test: search and fetch a known page.
5. Run write-boundary tests without altering valuable content:
   - a non-allowlisted update must be rejected locally;
   - a non-allowlisted create must be rejected locally.
6. With explicit test content, create a temporary child beneath the allowed parent, update that new page, and confirm its ID is persisted.
7. Restart Codex and Antigravity, then confirm `notion-safe-mcp` appears in each client's MCP inventory.

## Out of Scope

- OAuth or multi-user authentication.
- Database/data-source schema mutation.
- Deleting or trashing pages.
- Editing attachments, comments, users, or workspace settings.
- Automatically rotating or revoking the legacy token.

## Acceptance Criteria

- Both local clients can start the restored server without protocol errors.
- Search and fetch work for content shared with the integration.
- The five historical tool names are present.
- Writes outside the allowlist are rejected before a Notion mutation request.
- Allowed creation and subsequent update work with a disposable test page.
- No credential value appears in tracked files, tests, logs, or user-visible command output.

## References

- OpenAI MCP configuration: https://learn.chatgpt.com/docs/extend/mcp?surface=cli
- Notion authorization: https://developers.notion.com/guides/get-started/authorization
- Notion Markdown API: https://developers.notion.com/guides/data-apis/working-with-markdown-content
- Notion API version changes: https://developers.notion.com/reference/changes-by-version
