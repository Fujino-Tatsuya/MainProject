using System.Runtime.CompilerServices;

// 테스트 때문에 public API를 늘리지 않고 internal + InternalsVisibleTo를 사용한다. (PLAN §6)
[assembly: InternalsVisibleTo("BeaverLobby.Player.Dash.EditModeTests")]
