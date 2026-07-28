# Backlog

## Steam Lobby Invite Code

Priority: Later

Use an AccountID short-code approach for Steam lobby invites.

Decision:

- Encode the lower 32 bits of the Steam Lobby ID, the Account ID.
- Use Base36 for the user-facing invite code.
- Rebuild the full 64-bit Steam Lobby ID by combining fixed upper bits with the decoded Account ID.
- Do not implement yet because schedule does not allow proper Steam validation now.

Rationale:

- Base64 can produce shorter 6-character codes, but it is case-sensitive and may require padding or URL-safe handling.
- Base36 is slightly longer, up to 7 characters for a 32-bit value, but uses only uppercase letters and digits.
- Base36 is easier for users to type, paste, and read.
- This approach does not require a mapping server or Redis-style lookup table.

Required validation before implementation:

- Create several real Steam lobbies and confirm the upper 32 bits are stable for the target environment.
- Verify round-trip conversion:
  `lobbyId -> accountId -> base36Code -> accountId -> rebuiltLobbyId`.
- Confirm `JoinLobby(rebuiltLobbyId)` succeeds.
- Check whether Steam test, beta, or non-public universes change the fixed upper bits.

Open risk:

- If Steam Lobby ID upper bits are not stable in the target environment, AccountID-only codes will not be enough. In that case, encode the full 64-bit Lobby ID instead.
