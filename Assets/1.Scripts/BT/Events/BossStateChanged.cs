using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

#if UNITY_EDITOR
[CreateAssetMenu(menuName = "Behavior/Event Channels/BossStateChanged")]
#endif
[Serializable, GeneratePropertyBag]
[EventChannelDescription(name: "BossStateChanged", message: "Boss [State] is Changed", category: "Events", id: "0ac722cd1f48872da046a4832f460c4f")]
public sealed partial class BossStateChanged : EventChannel<TwentyThreeState> { }

