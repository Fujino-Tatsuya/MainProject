using System;
using Unity.Behavior;
using UnityEngine;
using Unity.Properties;

#if UNITY_EDITOR
[CreateAssetMenu(menuName = "Behavior/Event Channels/ReStart")]
#endif
[Serializable, GeneratePropertyBag]
[EventChannelDescription(name: "ReStart", message: "Someone Survived", category: "Events", id: "26a8d93da8030e5cb2fec4b7efb5efb8")]
public sealed partial class ReStart : EventChannel { }

