# Repository Map

> 자동 생성 파일 — 직접 수정하지 말 것. `node Tools/RepoMap/generate.js`로 재생성.
> 생성 근거: [PLAN.md](../../PLAN.md) 「레포지토리 맵(Repo Map) 생성 도구」.
> 표시 기준: interface는 전체 시그니처 노출(공개 계약). class는 `[wrapper]`로 태깅된
> 위임 메서드만 노출하고 나머지 내부 구현은 개수만 남긴다(Deep Module 원칙).

## Assets/1.Scripts/BT

### class NetworkSetAnimState : MonoBehaviour
  - (내부 메서드 2개 숨김)

### class ServerSetAnimState : NetworkBehaviour
  - void ServerSetInteger(string parameterName, int value)
  - void ServerSetInteger(string parameterName, T enumValue)
  - void ServerSetInteger(int id, int value)
  - void ServerSetInteger(int id, T enumValue)
  - void ServerSetTrigger(string parameterName)
  - void ServerSetTrigger(int id)
  - void ServerResetTrigger(string parameterName)
  - void ServerResetTrigger(int id)
  - void ServerSetFloat(string parameterName, float value)
  - void ServerSetFloat(int id, float value)
  - void ServerSetBool(string parameterName, bool value)
  - void ServerSetBool(int id, bool value)

## Assets/1.Scripts/BT/Actions

### class CalculateDistanceAction : Action
  - (내부 메서드 2개 숨김)

### class EnableColldierAction : Action
  - (내부 메서드 1개 숨김)

### class SetNumberWithTagAction : Action
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/BT/Actions/Animation

### class GetAnimClipPlayTimeAction : Action
  - (내부 메서드 2개 숨김)

### class SetAnimtorEnumAction : Action
  - (내부 메서드 2개 숨김)

### class WaitForAnimStateAction : Action
  - (내부 메서드 4개 숨김)

## Assets/1.Scripts/BT/Actions/Attack

### class AddRandomAttackAction : Action
  - (내부 메서드 2개 숨김)

### class BombAction : Action
  - (내부 메서드 1개 숨김)

### enum BombActionMode
  (메서드 없음)

### class GetRamdomAttackTypeAction : Action
  - (내부 메서드 2개 숨김)

### class HoldBombAction : Action
  - (내부 메서드 2개 숨김)

### class PageEventAction : Action
  - (내부 메서드 1개 숨김)

### class RemoveRandomAttackAction : Action
  - (내부 메서드 2개 숨김)

### class SetChargingStateAction : Action
  - (내부 메서드 2개 숨김)

### class ThrowBombAction : Action
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/BT/Actions/Enable

### class SetEnableBoxColliderAction : Action
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/BT/Actions/Event

### class ReStart : EventChannel
  (메서드 없음)

## Assets/1.Scripts/BT/Actions/Find

### class FindGroupsByTagAction : Action
  - (내부 메서드 2개 숨김)

### class FindNearestToAgentAction : Action
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/BT/Actions/GameObject

### class InstantiateNetworkObjectAction : Action
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/BT/Actions/Math

### class MinusFloatAction : Action
  - (내부 메서드 2개 숨김)

### class PlusFloatAction : Action
  - (내부 메서드 2개 숨김)

### class PlusIntAction : Action
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/BT/Actions/NavMesh

### class MoveTowardDirectionAction : Action
  - (내부 메서드 2개 숨김)

### class ResetPathAction : Action
  - (내부 메서드 2개 숨김)

### class ResetVelocityAction : Action
  - (내부 메서드 2개 숨김)

### class SetAgentDashModeAction : Action
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/BT/Actions/Physics

### class CheckCollisionInBoxAction : Action
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/BT/Actions/Return

### class ReturnFailAction : Action
  - (내부 메서드 1개 숨김)

### class ReturnRunningAction : Action
  - (내부 메서드 2개 숨김)

### class ReturnSuccessAction : Action
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/BT/Actions/Server

### class ServerResetTriggerIntAction : Action
  - (내부 메서드 2개 숨김)

### class ServerResetTriggerStringAction : Action
  - (내부 메서드 2개 숨김)

### class ServerSetAnimIntegerIntEnumAction : Action
  - (내부 메서드 2개 숨김)

### class ServerSetAnimIntegerIntIntAction : Action
  - (내부 메서드 2개 숨김)

### class ServerSetAnimIntegerStringEnumAction : Action
  - (내부 메서드 2개 숨김)

### class ServerSetAnimIntegerStringIntAction : Action
  - (내부 메서드 2개 숨김)

### class ServerSetTriggerIntAction : Action
  - (내부 메서드 2개 숨김)

### class ServerSetTriggerStringAction : Action
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/BT/Actions/Timer

### class AddDeltaTimeAction : Action
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/BT/Actions/Transform

### class GetSpawnPointAction : Action
  - (내부 메서드 2개 숨김)

### class KnockbackAttackAction : Action
  - (내부 메서드 2개 숨김)

### class LookAtRotateAction : Action
  - (내부 메서드 3개 숨김)

### class MoveForDurationAction : Action
  - (내부 메서드 7개 숨김)

### class SetPositionThroughRaycastAction : Action
  - (내부 메서드 2개 숨김)

### class SetPositionToTargetAction : Action
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/BT/Actions/Unit

### class GetPlayerCountAction : Action
  - (내부 메서드 1개 숨김)

### class IncreaseUnitHpAction : Action
  - (내부 메서드 2개 숨김)

### class IncreaseUnitShieldAction : Action
  - (내부 메서드 2개 숨김)

### class RemoveDeadUnitsFromGroupAction : Action
  - (내부 메서드 3개 숨김)

## Assets/1.Scripts/BT/Conditions

### class CheckArrivalInChargeControllerCondition : Condition
  - bool IsTrue()
  - (내부 메서드 1개 숨김)

### class CheckCollisionInBoxCondition : Condition
  - bool IsTrue()

### class CheckDefeatInChargeControllerCondition : Condition
  - bool IsTrue()
  - (내부 메서드 1개 숨김)

### class CheckHealthPercentCondition : Condition
  - bool IsTrue()
  - (내부 메서드 1개 숨김)

### class IsCurrentAnimStateEqualTooStateNameCondition : Condition
  - void OnStart() → `UnityEngine.Animator.StringToHash` 위임 [wrapper?]
  - bool IsTrue()
  - (내부 메서드 1개 숨김)

### class IsGameObjectNotNullCondition : Condition
  - bool IsTrue()

### class IsGameObjectNullCondition : Condition
  - bool IsTrue()

### class IsTargetOnRightSideCondition : Condition
  - bool IsTrue()

## Assets/1.Scripts/BT/Events

### class BossStateChanged : EventChannel<TwentyThreeState>
  (메서드 없음)

## Assets/1.Scripts/Camera

### class CameraTargetSwitcher : MonoBehaviour
  - void FocusOwnerPlayer()
  - void EnterFallView()
  - void EnterFallView(float fixedWorldY)
  - void ReturnToPlayerView()
  - void SwitchToNextTarget() → `SwitchTarget` 위임 [wrapper?]
  - void SwitchToPreviousTarget() → `SwitchTarget` 위임 [wrapper?]
  - void SetSpectatorMode(bool enabled)
  - void BindOwnerLifeCycleFromCurrentTarget() → `BindOwnerLifeCycle` 위임 [wrapper?]
  - void HandleOwnerLifeStateChanged(PlayerLifeState previousState, PlayerLifeState currentState) → `SetSpectatorMode` 위임 [wrapper?]
  - void RestoreFixedCameraRotation() → `ApplyFixedCameraRotation` 위임 [wrapper?]
  - (내부 메서드 21개 숨김)

### class CameraTestPlayer : NetworkBehaviour
  - void Awake() → `GetComponent<Renderer>` 위임 [wrapper?]
  - void OnNetworkSpawn() → `ApplyClientColor` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class FloatFollowTarget : MonoBehaviour
  - void SetSource(Transform newSource) → `RefreshPosition` 위임 [wrapper?]
  - void SetFixedWorldY(float worldY) → `RefreshPosition` 위임 [wrapper?]
  - void LateUpdate() → `RefreshPosition` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/Camera/Feedback

### class CameraFeedback : MonoBehaviour
  - void ReportLocalPlayerHit() → `TryGenerateImpulse` 위임 [wrapper?]
  - void ReportLocalPlayerDealtDamage() → `TryGenerateImpulse` 위임 [wrapper?]
  - (내부 메서드 7개 숨김)

### class UnitCameraFeedbackReporter : MonoBehaviour
  - void Awake() → `GetComponent<Unit>` 위임 [wrapper?]
  - (내부 메서드 6개 숨김)

## Assets/1.Scripts/Dev

### class DevSceneBooter : MonoBehaviour
  - void Boot()
  - (내부 메서드 7개 숨김)

### class HitVFXDebugHUD : MonoBehaviour
  - string Describe(EffectCatalog.HitVFXType? type) → `type.Value.ToString` 위임 [wrapper]
  - (내부 메서드 6개 숨김)

## Assets/1.Scripts/Dev/Editor

### class DevBuildSceneList
  - void EnableDevScenes() → `SetDevScenesEnabled` 위임 [wrapper?]
  - void DisableDevScenes() → `SetDevScenesEnabled` 위임 [wrapper?]
  - void RemoveDevBootScene()
  - void LogCurrentList() → `Debug.Log` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/Dev/Profiler

### class Prof
  (메서드 없음)

### class ProfilerHUD : MonoBehaviour
  - (내부 메서드 14개 숨김)

### enum Corner
  (메서드 없음)

### class MarkerSpec
  (메서드 없음)

## Assets/1.Scripts/Dev/Profiler/Editor

### class ProfilerWindow : EditorWindow
  - void Open()
  - void OnDisable() → `DisposeRecorders` 위임 [wrapper?]
  - ProfilerRecorder TryStartFirst() → `TryStart` 위임 [wrapper?]
  - (내부 메서드 19개 숨김)

### class Cat
  (메서드 없음)

### class Counter
  (메서드 없음)

### class Pass
  (메서드 없음)

### class GraphElement : VisualElement
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/Editor

### class BuildWindowsPlayer
  - void BuildWindows64FromMenu() → `Build` 위임 [wrapper?]
  - void BuildWindows64()
  - string ResolveOutputPath(string cliValue) → `Path.GetFullPath` 위임 [wrapper?]
  - (내부 메서드 3개 숨김)

### class PlayerInterruptSkillAuthoring
  - void Wire()
  - (내부 메서드 4개 숨김)

## Assets/1.Scripts/Effects

### class EffectCatalog : ScriptableObject
  - EffectEntry GetHitEffect(HitVFXType hitVFX)
  - (내부 메서드 1개 숨김)

### enum HitVFXType
  (메서드 없음)

### class EffectDurationProbe : MonoBehaviour
  - void Measure()
  - (내부 메서드 2개 숨김)

### class EffectEntry : ScriptableObject
  - void OnValidate() → `RecomputeLifetimes` 위임 [wrapper]
  - bool RecomputeLifetimes()
  - (내부 메서드 2개 숨김)

### struct EffectHandle : IEquatable<EffectHandle>
  - bool Equals(EffectHandle other)
  - bool Equals(object obj) → `Equals` 위임 [wrapper]
  - int GetHashCode()
  - string ToString()

### class EffectHitPoint
  - void ResetWarnings() → `Warned.Clear` 위임 [wrapper]
  - Pose Resolve(HitPointMode mode, HitPointInfo hitInfo)
  - (내부 메서드 3개 숨김)

### enum HitPointMode
  (메서드 없음)

### struct HitPointInfo
  (메서드 없음)

### class EffectInstance : MonoBehaviour
  (메서드 없음)

### class EffectLifetime
  - float Estimate(EffectPart[] parts)
  - float PrefabLifetime(GameObject prefab)
  - float Max(ParticleSystem.MinMaxCurve curve)
  - (내부 메서드 2개 숨김)

### class EffectManager : MonoBehaviour
  - EffectEntry GetHitEffect(EffectCatalog.HitVFXType hitVFXType)
  - void Play(EffectEntry entry, Vector3 position, Quaternion rotation)
  - void Play(EffectEntry entry, Vector3 position) → `Play` 위임 [wrapper]
  - EffectHandle PlayLooping(EffectEntry entry, Transform follow, Vector3 offset) → `PlayLoopingCore` 위임 [wrapper?]
  - EffectHandle PlayLooping(EffectEntry entry, Vector3 position, Quaternion rotation) → `PlayLoopingCore` 위임 [wrapper?]
  - void Release(EffectHandle handle)
  - void ReleaseImmediate(EffectHandle handle)
  - void SetPlayRateForTarget(Transform target, float rate)
  - IEffectSystem DriverOf(GameObject instance) → `instance.GetComponent<EffectInstance>` 위임 [wrapper?]
  - int PoolCountAll(GameObject prefab) → `_pool.CountAll` 위임 [wrapper]
  - int PoolCountActive(GameObject prefab) → `_pool.CountActive` 위임 [wrapper]
  - (내부 메서드 18개 숨김)

### class ActiveEffect
  (메서드 없음)

### struct SpawnedPart
  (메서드 없음)

### struct PendingPart
  (메서드 없음)

### class EffectPart
  (메서드 없음)

### class EffectPool
  - GameObject Rent(GameObject prefab) → `PoolFor(prefab).Get` 위임 [wrapper?]
  - void Return(GameObject instance)
  - void Prewarm(GameObject prefab, int count)
  - int CountAll(GameObject prefab) → `_pools.TryGetValue` 위임 [wrapper?]
  - int CountActive(GameObject prefab) → `_pools.TryGetValue` 위임 [wrapper?]
  - void Dispose() → `_pools.Clear` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class EffectPrefabRules
  - void ResetWarnings() → `Warned.Clear` 위임 [wrapper]
  - bool ValidateAndFix(GameObject instance, GameObject sourcePrefab)

### class EffectSceneTester : MonoBehaviour
  - void Case1OneShot()
  - void Case2Composite()
  - void Case4PlayLoop()
  - void Case5PlayAttachedLoop()
  - void CaseReleaseLoop()
  - void Case5DestroyTarget()
  - void Case6Burst()
  - void Case6FreezeTarget() → `SetTargetRate` 위임 [wrapper]
  - void Case6ResumeTarget() → `SetTargetRate` 위임 [wrapper]
  - void LogPoolStats()
  - void Case7HitPoint()
  - (내부 메서드 7개 숨김)

### class EffectTestMover : MonoBehaviour
  - void Update() → `Mathf.Sin` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### class HitVFXPlayback
  - void ResetWarnings() → `Warned.Clear` 위임 [wrapper]
  - void Play(Component context, Collider hitVFXCollider, HitVFXType hitVFXType, Vector3 sourcePosition)

### interface IEffectSystem
  - bool CanDrive(GameObject instance)
  - void Play(GameObject instance)
  - void Stop(GameObject instance, bool immediate)
  - void SetPlayRate(GameObject instance, float rate)
  - void ResetForPool(GameObject instance)

### class ShurikenEffectSystem : IEffectSystem
  - bool CanDrive(GameObject instance)
  - void Play(GameObject instance)
  - void Stop(GameObject instance, bool immediate)
  - void SetPlayRate(GameObject instance, float rate)
  - void ResetForPool(GameObject instance)
  - bool IsAlive(GameObject instance)
  - (내부 메서드 2개 숨김)

### class ShurikenPartCache : MonoBehaviour
  (메서드 없음)

## Assets/1.Scripts/Effects/Editor

### class EffectDurationProbeEditor : Editor
  - void OnInspectorGUI()

### class EffectEntryEditor : Editor
  - void OnInspectorGUI()
  - (내부 메서드 1개 숨김)

### class EffectEntryPostprocessor : AssetPostprocessor
  - (내부 메서드 2개 숨김)

### class EffectSceneTesterEditor : Editor
  - void OnInspectorGUI()
  - void Section(string title) → `EditorGUILayout.LabelField` 위임 [wrapper?]

### class EffectSystemSetup
  - void Run()
  - void RecomputeAll()
  - void Smoke()
  - (내부 메서드 5개 숨김)

## Assets/1.Scripts/Enemy

### enum DistanceState
  (메서드 없음)

### class Enemy : Unit
  - void OnNetworkSpawn()
  - bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
  - void PlayHitVFXRpc(Vector3 sourcePosition) → `HitVFXPlayback.Play` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### class EnemyBTActivator : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OpenBT()
  - void CloseBT()
  - void RaiseRestart()
  - (내부 메서드 5개 숨김)

### enum AreaType
  (메서드 없음)

### class FloorAreaEffect : MonoBehaviour
  - void OverlapGrow()
  - void OverTimeGrow()
  - void StartOverTimeGrow(float duration, Vector3 targetScale)
  - (내부 메서드 2개 숨김)

### enum GroggyState
  (메서드 없음)

### enum JumpState
  (메서드 없음)

### class MonsterTimeController : MonoBehaviour
  - void SetTimeScale(float scale)
  - void ResetTimeScale() → `SetTimeScale` 위임 [wrapper]
  - void HitStop(float duration)
  - void SlowMotion(float targetScale, float easeInTime, float holdTime, float easeOutTime)
  - (내부 메서드 6개 숨김)

### class RunningOnlyOnServer : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn() → `base.OnNetworkDespawn` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### enum TrashMobState
  (메서드 없음)

## Assets/1.Scripts/Enemy/Boss

### class BaseAttackChoice : MonoBehaviour
  - int GetRandomAttack(float currentDistance)
  - void AddType(T type)
  - void RemoveType(T type)
  - void PageEvent(int page)

### class Bomb : MonoBehaviour, IAttackReceiver
  - bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)

### enum BombState
  (메서드 없음)

### class BombController : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - void OnDrawGizmos() → `Gizmos.DrawWireSphere` 위임 [wrapper?]
  - void Hold(Transform socket)
  - void Launch(Vector3 target, float duration, float arcHeight)
  - void Explode() → `SetBombEnableClientRpc` 위임 [wrapper?]
  - void SetBombEnableClientRpc(bool enable) → `SetBombEnable` 위임 [wrapper?]
  - void SetFloorEnableClientRpc(bool enable) → `SetFloorEnable` 위임 [wrapper?]
  - void SetEnableClientRpc(bool enable) → `gameObject.SetActive` 위임 [wrapper?]
  - (내부 메서드 19개 숨김)

### class BombLauncher : MonoBehaviour
  - void SetThrowFigures(Vector3 localDirection, float distance, float duration, float arc, float spread)
  - void BombHold()
  - void BombThrow()
  - void BombDestroy()
  - (내부 메서드 1개 숨김)

### class ChargeController : NetworkBehaviour, IDamageSettable
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - void SetDamage(int value) → `_floorColliderAttack.SetDamage` 위임 [wrapper?]
  - void SetList(List<ChargingObject> list)
  - void SetFloorEnableClientRpc(bool enable) → `SetFloorActive` 위임 [wrapper?]
  - void StartCharge(int playerCount)
  - void EndCharge()
  - (내부 메서드 5개 숨김)

### class ChargingObject : Unit
  - void Awake() → `CacheLocalPositions` 위임 [wrapper?]
  - void OnNetworkSpawn()
  - void SetMinMaxY(float rise) → `Mathf.Max` 위임 [wrapper?]
  - void TakeDamage(AttackInfo attackInfo)
  - void StartCharge()
  - void EndCharge()
  - void BeginLowering() → `SetColliderEnabled` 위임 [wrapper?]
  - (내부 메서드 5개 숨김)

### enum ChargeState
  (메서드 없음)

### enum TriggerMode
  (메서드 없음)

### class ColliderBasicAttack : BaseAttack
  - void Awake() → `GetComponent<KnockbackAttack>` 위임 [wrapper?]
  - void OnTriggerEnter(Collider other) → `OnAttackTriggerEnter` 위임 [wrapper?]
  - void OnAttackTriggerEnter(Collider other)
  - void OnTriggerStay(Collider other) → `OnAttackTriggerStay` 위임 [wrapper?]
  - void OnAttackTriggerStay(Collider other)
  - void OnTriggerExit(Collider other) → `OnAttackTriggerExit` 위임 [wrapper?]
  - void OnAttackTriggerExit(Collider other)

### class GrabController : NetworkBehaviour
  - void SetGrabFigures(int grabPercentage, int holdPercentage, int landingPercentage, float attackPeriod)
  - void Detect()
  - void Throw()
  - (내부 메서드 6개 숨김)

### class JumpController : NetworkBehaviour, IDamageSettable
  - void SetCinematicLandingMode(bool enabled)
  - void OnNetworkSpawn()
  - void SetTarget()
  - void OnLanded()
  - void SetDamage(int value) → `Mathf.Max` 위임 [wrapper?]
  - void EnableMeshRenderers(bool enable) → `mesh.SetActive` 위임 [wrapper?]
  - void ShowMyMeshClientRpc(bool enable) → `EnableMeshRenderers` 위임 [wrapper?]
  - void HideFloorsClientRpc() → `SetFloorsEnable` 위임 [wrapper?]
  - (내부 메서드 5개 숨김)

### class KnockbackAttack : BaseAttack, IKnockbackSettable
  - void Awake() → `InitializeAttackInfo` 위임 [wrapper?]
  - void SetKnockbackStrength(float value) → `Mathf.Max` 위임 [wrapper?]
  - void ApplyKnockbackAttack(GameObject collidedObject)
  - (내부 메서드 1개 숨김)

### class TriggerKnockbackAttack : MonoBehaviour
  - (내부 메서드 2개 숨김)

### class TwentyThreeAnimEvents : NetworkBehaviour
  - void TryGrabEvent()
  - void ThrowEvent()
  - void SetTargetEvent()
  - void FallEvent()
  - void OnLandedEvent()
  - (내부 메서드 1개 숨김)

### struct WeightedAttack
  (메서드 없음)

### class WellsAnimEvents : MonoBehaviour
  - void ThrowBombEvent()
  - void BombDestroyEvent()
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/Enemy/Boss/Wells&No.23

### class TwentyThreeArenaContext : NetworkBehaviour
  - void OnNetworkSpawn()

### class TwentyThreeBasicAttackChoice : BaseAttackChoice
  - void AddType(T type)
  - void RemoveType(T type)
  - int GetRandomAttack(float currentDistance)
  - void PageEvent(int page)
  - (내부 메서드 4개 숨김)

### enum TwentyThreeBasicAttackType
  (메서드 없음)

### enum TwentyThreeState
  (메서드 없음)

### class TwentyThreeWells_Initializer : NetworkBehaviour
  - void OnNetworkSpawn()
  - void ApplyDamages() → `Edit.LogError` 위임 [wrapper?]
  - void ApplyKnockbacks() → `Edit.LogError` 위임 [wrapper?]
  - (내부 메서드 5개 숨김)

### struct DamageEntry
  (메서드 없음)

### struct KnockbackEntry
  (메서드 없음)

### enum WellsState
  (메서드 없음)

## Assets/1.Scripts/Loading

### class NetworkLoadingFlowController : MonoBehaviour
  - void Awake() → `Debug.Log` 위임 [wrapper?]
  - void RegisterNetworkCallbacks()
  - void StartGameLoading()
  - void RegisterView(NetworkLoadingScreenView view) → `ApplyViewState` 위임 [wrapper?]
  - void SetDefaultPlayerPrefab(GameObject playerPrefab)
  - void SpawnAllPlayers()
  - void StartLocalProgressReporting() → `StartCoroutine` 위임 [wrapper?]
  - float CalculateLocalLoadingProgress() → `GetLocalSceneLoadProgress` 위임 [wrapper?]
  - void SetEditorDefaults(string loadingScene, string targetScene, float minimumSeconds, float readySeconds)
  - IEnumerator UnloadLoadingScene() → `UnloadNetworkScene` 위임 [wrapper?]
  - (내부 메서드 39개 숨김)

### enum NetworkLoadingPhase : byte
  (메서드 없음)

### class NetworkLoadingScreenView : MonoBehaviour
  - void OnEnable() → `StartCoroutine` 위임 [wrapper?]
  - void SetProgress(float progress) → `ApplyProgress` 위임 [wrapper?]
  - void SetPhase(NetworkLoadingPhase phase) → `ApplyPhaseText` 위임 [wrapper?]
  - void CompleteAndDestroy() → `Destroy` 위임 [wrapper?]
  - void SetEditorReferences(Image fill, Image center, TMP_Text tooltip, TMP_Text status, TMP_Text percent)
  - (내부 메서드 12개 숨김)

## Assets/1.Scripts/Lobby

### class LobbyPlayerSlotView : MonoBehaviour
  - void SetState(bool connected, bool ready) → `SetReady` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class LobbyUIController : MonoBehaviour
  - void ToggleLocalReady() → `SetLocalReady` 위임 [wrapper?]
  - void SetLocalReady(bool ready)
  - (내부 메서드 21개 숨김)

## Assets/1.Scripts/Managers

### class GameManager : MonoBehaviour
  - void SuppressStartupSceneLoad()
  - void NotifyMainGameReady()
  - void SubscribeMainGameReady(Action callback)
  - void UnsubscribeMainGameReady(Action callback)
  - void SubscribeMainGameStart(Action callback)
  - void UnsubscribeMainGameStart(Action callback) → `Instance?.UnsubscribeMainGameReady` 위임 [wrapper?]
  - void GoToLobby()
  - void GoToLobbyButton()
  - void GoToResult()
  - void GoToResultButton()
  - (내부 메서드 9개 숨김)

### enum GameState
  (메서드 없음)

### class LobbySceneManager : NemoSceneManager
  - void ApplyConnectionData() → `TryApplyConnectionData` 위임 [wrapper?]
  - void StartHost()
  - void StartClient()
  - void SelectDirectMode()
  - void SelectRelayMode()
  - void StartRelayHost()
  - void StartRelayJoin()
  - void ToggleReady()
  - void StartGameLoading()
  - void HandleLobbyStateChanged() → `ApplyRoleUi` 위임 [wrapper?]
  - (내부 메서드 29개 숨김)

### class MapSceneManager : NemoSceneManager
  - void GoToResult()
  - void ConfirmClientExit()
  - void CancelClientExit() → `SetWarningPanel` 위임 [wrapper?]
  - void OpenOptionPanel() → `SetOptionPanel` 위임 [wrapper?]
  - void CloseOptionPanel() → `SetOptionPanel` 위임 [wrapper?]
  - void ToggleOptionPanel() → `SetOptionPanel` 위임 [wrapper?]
  - (내부 메서드 16개 숨김)

### class NemoSceneManager : MonoBehaviour
  - void Awake() → `ResolveCommonReferences` 위임 [wrapper?]
  - void FadeIn() → `StartFade` 위임 [wrapper?]
  - void FadeOut() → `StartFade` 위임 [wrapper?]
  - Button FindButton(string objectName) → `target.GetComponent<Button>` 위임 [wrapper?]
  - void WarnMissingReference(string referenceName) → `Debug.LogWarning` 위임 [wrapper?]
  - void BeginTransition() → `Debug.Log` 위임 [wrapper?]
  - void EndTransition() → `Debug.Log` 위임 [wrapper?]
  - void StartFade(float targetAlpha) → `StartCoroutine` 위임 [wrapper?]
  - (내부 메서드 12개 숨김)

### class PartyWipeWatcher : MonoBehaviour
  - (내부 메서드 3개 숨김)

### class ResultSceneManager : NemoSceneManager
  - void GoToLobby()
  - (내부 메서드 4개 숨김)

### class SessionResult
  - void Capture(bool cleared, float survivalSeconds, int kills)
  - void Clear()
  - string FormatSurvival() → `Mathf.FloorToInt` 위임 [wrapper?]

### class SessionStatsTracker : MonoBehaviour
  - void Capture(bool cleared)
  - bool HasAnyPlayer() → `FindAnyObjectByType<PlayerLifeCycleController>` 위임 [wrapper?]
  - (내부 메서드 6개 숨김)

### class TitleOptionsPanel : MonoBehaviour
  - void OnEnable() → `ShowGameplay` 위임 [wrapper?]
  - void ShowGameplay() → `ShowOnly` 위임 [wrapper?]
  - void ShowGraphics() → `ShowOnly` 위임 [wrapper?]
  - void ShowControls() → `ShowOnly` 위임 [wrapper?]
  - void ShowAudio() → `ShowOnly` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class TitleSceneManager : NemoSceneManager
  - void StartGame()
  - void ToggleOption() → `SetOptionPanel` 위임 [wrapper?]
  - void OpenOption() → `SetOptionPanel` 위임 [wrapper?]
  - void CloseOption() → `SetOptionPanel` 위임 [wrapper?]
  - void ExitGame()
  - void SetTitleButtonsInteractable(bool interactable) → `SetButtonsInteractable` 위임 [wrapper?]
  - (내부 메서드 8개 숨김)

### class UiInputGateManager
  - void Acquire(object token)
  - void Release(object token)

## Assets/1.Scripts/Managers/Editor

### class GameManagerMainGameReadyTests
  - void SetUp() → `_gameObject.AddComponent<GameManager>` 위임 [wrapper?]
  - void TearDown() → `Object.DestroyImmediate` 위임 [wrapper?]
  - void NotifyMainGameReady_IsIdempotent()
  - void LeavingMainGame_ResetsReadyState()
  - void LateSubscriber_CanHandleReadyFromCurrentState()
  - void SetState(GameManager.GameState state) → `SetStateMethod.Invoke` 위임 [wrapper?]

## Assets/1.Scripts/Map

### class BossArenaContext : MonoBehaviour
  - void Awake() → `Resolve` 위임 [wrapper]
  - void Resolve()
  - void Validate()
  - BossArenaContext FindInScene(Object context)
  - Transform FindChildByName(string childName) → `GetComponentsInChildren<Transform>` 위임 [wrapper?]
  - Collider FindChildColliderByTag(string tag) → `GetComponentsInChildren<Collider>` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### class BossEncounterDirector : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - void OnDestroy()
  - Transform FindLandingPointByName() → `GameObject.Find` 위임 [wrapper?]
  - void BeginDescent() → `SetPhase` 위임 [wrapper?]
  - void BeginCombatServer()
  - void AbortEncounterServer(string reason)
  - (내부 메서드 31개 숨김)

### enum BossEncounterPhase
  (메서드 없음)

### class BossEnterTrigger : MonoBehaviour
  - void Awake() → `GetComponent<BoxCollider>` 위임 [wrapper?]
  - void OnTriggerEnter(Collider other) → `Track` 위임 [wrapper]
  - void OnTriggerExit(Collider other) → `Track` 위임 [wrapper]
  - (내부 메서드 3개 숨김)

### class BossEnterZoneVisual : MonoBehaviour
  - void Setup(Vector3 centerLocal, Vector2 sizeXZ)
  - (내부 메서드 3개 숨김)

### class BossTeleportManager : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - void OnDestroy()
  - void SetOccupied(bool occupied)
  - (내부 메서드 14개 숨김)

### class ConveyorGroup : MonoBehaviour
  - void OnValidate() → `Mathf.Max` 위임 [wrapper?]

### class ConveyorTile : MonoBehaviour, ISurfaceCarrier
  - void Awake() → `ResolveGroup` 위임 [wrapper?]
  - Vector3 GetCarryDelta(Vector3 riderWorldPos, float dt)
  - void ResolveGroup() → `GetComponentInParent<ConveyorGroup>` 위임 [wrapper?]
  - (내부 메서드 8개 숨김)

### enum TileKind
  (메서드 없음)

### enum CardinalDirection
  (메서드 없음)

### struct GeneratedNodeData : INetworkSerializable
  - void NetworkSerialize(BufferSerializer<T> serializer)

### class GeneratedZoneIdentity : MonoBehaviour
  (메서드 없음)

### interface ISurfaceCarrier
  - Vector3 GetCarryDelta(Vector3 riderWorldPos, float dt)

### struct ZonePlacement
  (메서드 없음)

### class LayoutPlacer : MonoBehaviour
  - List<ZonePlacement> SelectLayouts(List<ZoneSlot> slots, ZoneLayoutCatalogSO catalog, int difficulty, System.Random rng)
  - (내부 메서드 1개 숨김)

### class MapContentSpawner : MonoBehaviour
  - void SpawnPlacements(MapGenerator gen, List<ZonePlacement> placements)
  - void ClearGenerated()
  - (내부 메서드 9개 숨김)

### enum ZoneGrade : byte
  (메서드 없음)

### enum ZoneType : byte
  (메서드 없음)

### enum ZoneRole : byte
  (메서드 없음)

### enum NodeTier : byte
  (메서드 없음)

### enum NodeContentType : byte
  (메서드 없음)

### enum MonsterBehavior : byte
  (메서드 없음)

### enum ClearCondition : byte
  (메서드 없음)

### enum Difficulty : byte
  (메서드 없음)

### enum ZoneSize : byte
  (메서드 없음)

### class MapGenConfigSO : ScriptableObject
  (메서드 없음)

### class MapGenerator : MonoBehaviour
  - List<ZonePlacement> Generate(int mapSeed, int difficultyLevel)
  - List<ZonePlacement> ComputePlacements(int mapSeed, int difficultyLevel)
  - ZoneSlot GetRoleSlot(ZoneRole role)
  - void EditorTestGenerate() → `Generate` 위임 [wrapper]
  - (내부 메서드 4개 숨김)

### class MapNavMeshBaker : MonoBehaviour
  - void HandleGenerated(MapGenerator gen) → `Bake` 위임 [wrapper]
  - void RebakeNow(string reason) → `Bake` 위임 [wrapper]
  - void ReattachAgents() → `Object.FindObjectsByType<NavMeshAgent>` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### class MapNetworkSync : NetworkBehaviour
  - int ComposeDifficultyLevel() → `Mathf.Max` 위임 [wrapper]
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - (내부 메서드 2개 숨김)

### class MapOverviewUI : MonoBehaviour
  - void Toggle()
  - void Show()
  - void Hide() → `DestroyCanvas` 위임 [wrapper?]
  - void OnDestroy() → `DestroyCanvas` 위임 [wrapper?]
  - void RefreshOverview()
  - (내부 메서드 6개 숨김)

### class MapPrefabCatalogSO : ScriptableObject
  - List<GameObject> GetPool(NodeTier tier, NodeContentType content)
  - int PickVariantIndex(System.Random rng, NodeTier tier, NodeContentType content)
  - GameObject GetPrefab(NodeTier tier, NodeContentType content, int variantIndex)

### struct MonsterGroupData
  (메서드 없음)

### class MovingPlatform : MonoBehaviour, ISurfaceCarrier
  - Vector3 GetCarryDelta(Vector3 riderWorldPos, float dt)
  - void EditorPreviewBegin()
  - void EditorPreviewTick(double elapsed)
  - void EditorPreviewEnd()
  - bool IsViaNode(Transform wp) → `wp.GetComponent<WaypointNode>` 위임 [wrapper?]
  - (내부 메서드 10개 숨김)

### enum PathMode
  (메서드 없음)

### struct Segment
  (메서드 없음)

### class NodeMarker : MonoBehaviour
  - (내부 메서드 1개 숨김)

### class SpawnPoint : MonoBehaviour
  - void ResetRuntime()

### class Vent : MonoBehaviour
  - void Awake() → `SetDamageColliderActive` 위임 [wrapper?]
  - void EditorPreviewBegin()
  - void EditorPreviewTick(double elapsed)
  - void EditorPreviewEnd()
  - (내부 메서드 5개 숨김)

### enum VentState
  (메서드 없음)

### class WaypointNode : MonoBehaviour
  - float ResolvePause(float fallback)

### enum NodeType
  (메서드 없음)

### class ZoneBridgeGate : MonoBehaviour
  - void SetSlotID(int slotID)
  - void SetPanelActivatedVisual(int index, bool activated)
  - bool TryGetPanelPosition(int index, Vector3 position)
  - void ApplyOpenProgress(float progress)
  - int CountUnauthoredSegments()
  - (내부 메서드 6개 숨김)

### struct Segment
  (메서드 없음)

### class BakeOpenScope : System.IDisposable
  - BakeOpenScope Begin()
  - void Dispose() → `gate.ApplyOpenProgress` 위임 [wrapper?]

### class ZoneBridgeGateManager : NetworkBehaviour
  - void OnDestroy()
  - void OnNetworkSpawn()
  - void OnNetworkDespawn() → `base.OnNetworkDespawn` 위임 [wrapper?]
  - void RegisterGate(ZoneBridgeGate gate)
  - void UnregisterGate(ZoneBridgeGate gate)
  - void HandleGatesChanged(NetworkListEvent<GateState> _) → `ApplyAllStates` 위임 [wrapper]
  - void ApplyAllStates() → `ApplyState` 위임 [wrapper?]
  - (내부 메서드 13개 숨김)

### struct GateState : INetworkSerializable, System.IEquatable<GateState>
  - void NetworkSerialize(BufferSerializer<T> s)
  - bool Equals(GateState o) → `OpenStartServerTime.Equals` 위임 [wrapper]

### class ZoneInteractRing : MonoBehaviour
  - ZoneInteractRing Create(Transform panel, float radius, Color color, float width, float groundLift, GameObject customPrefab)
  - void SetVisible(bool visible)
  - (내부 메서드 2개 숨김)

### struct MonsterSpawnEntry
  (메서드 없음)

### class ZoneLayout : MonoBehaviour
  - IEnumerable<MonsterSpawnEntry> ResolveSpawnEntries()
  - void OnDrawGizmosSelected() → `ResolveSpawnEntries` 위임 [wrapper?]

### class ZoneLayoutCatalogSO : ScriptableObject
  - List<GameObject> GetCombatPool(ZoneSize size, int difficulty)
  - GameObject GetRoleLayout(ZoneRole role, ZoneSize size)
  - List<GameObject> GetRolePool(ZoneRole role, ZoneSize size)

### struct Entry
  (메서드 없음)

### class ZoneSlot : MonoBehaviour
  - void ResetRuntime()
  - bool TryGetYaw(GameObject prefab, int yawSteps)
  - bool TryGetPosition(GameObject prefab, Vector3 position)
  - void SetPlacement(GameObject prefab, int yawSteps, Vector3 position)
  - (내부 메서드 1개 숨김)

### struct RotationEntry
  (메서드 없음)

## Assets/1.Scripts/Map/Editor

### class BossArenaWiring
  - void WireBossArena()
  - Transform FindChild(GameObject root, string childName) → `root.GetComponentsInChildren<Transform>(true)
               .FirstOrDefault` 위임 [wrapper]
  - (내부 메서드 4개 숨김)

### class BossEncounterWiring
  - void WireBossEncounter()
  - Transform FindLandingPoint() → `Object.FindObjectsByType<Transform>` 위임 [wrapper?]
  - (내부 메서드 3개 숨김)

### class BossRoomAuthoring
  - void RebuildBossRoomBounds()
  - void SetupBossChargePillars()
  - (내부 메서드 9개 숨김)

### class GroundLayerAuthoring
  - void DryRun() → `Execute` 위임 [wrapper]
  - void Apply() → `Execute` 위임 [wrapper]
  - void NarrowPlayerGroundMask()
  - string Normalize(string name) → `name.Substring` 위임 [wrapper?]
  - void Bump(SortedDictionary<string, int> map, string key) → `map.TryGetValue` 위임 [wrapper?]
  - (내부 메서드 5개 숨김)

### struct Stat
  (메서드 없음)

### class MapColliderAuthoring
  - void AddFloorWallColliders()
  - void RebuildStairRamps()
  - void AddWalkableModelInstanceColliders()
  - bool ContainsAny(string lowerName, string[] keywords) → `lowerName.Contains` 위임 [wrapper?]
  - void AddMeshCollidersToActiveSceneHallway()
  - (내부 메서드 6개 숨김)

### class MapMonsterAuthoring
  - void RemapZoneMonsterGroupIds()
  - void MigrateSpawnPointsToEntries()
  - void AuthorZoneLayouts()
  - void AuthorMonsterGroups()
  - (내부 메서드 7개 숨김)

### class MovingPlatformEditor : Editor
  - void OnInspectorGUI()
  - (내부 메서드 6개 숨김)

### class QuestLaserBlockerAuthoring
  - void RemoveLaserBlockers()
  - void SetupLaserBlockers()
  - (내부 메서드 2개 숨김)

### class SavePlacements
  - (내부 메서드 1개 숨김)

### class SlotAuthoringCleanup
  - void CleanupDeadRefs()
  - void TrimUnreachable()
  - (내부 메서드 5개 숨김)

### class SlotAuthoringModel
  - ZoneLayoutCatalogSO LoadCatalog()
  - List<ZoneSlot> GatherSceneSlots()
  - List<SlotPlan> BuildPlans(List<ZoneSlot> slots, ZoneLayoutCatalogSO catalog, int difficulty)
  - bool IsAuthored(ZoneSlot slot, GameObject prefab) → `slot.TryGetYaw` 위임 [wrapper]
  - int CountDeadEntries(ZoneSlot slot) → `slot.Rotations.Count` 위임 [wrapper]
  - (내부 메서드 3개 숨김)

### class SlotPlan
  (메서드 없음)

### class SlotAuthoringValidator
  - void Validate()

### class VentEditor : Editor
  - void OnInspectorGUI()
  - (내부 메서드 6개 숨김)

### class ZoneBridgeGateManagerWiring
  - void WireManager()

### class ZoneBridgeGateWiring
  - void WireGate()
  - void EstimateOpenPositions()
  - void RecordClosedPositions()
  - void RecordOpenPositions()
  - List<Transform> Collect(GameObject root, string prefix) → `root.GetComponentsInChildren<Transform>(true)
               .Where(t => t != root.transform && t.name.StartsWith(prefix))
               .OrderBy(t => t.name)
               .ToList` 위임 [wrapper]
  - string Names(List<Transform> list) → `string.Join` 위임 [wrapper]
  - (내부 메서드 5개 숨김)

### class ZoneRotationAuthoringWindow : EditorWindow
  - void Open() → `GetWindow<ZoneRotationAuthoringWindow>` 위임 [wrapper]
  - void OnEnable() → `Refresh` 위임 [wrapper]
  - void OnDisable() → `ClearSpawn` 위임 [wrapper]
  - (내부 메서드 6개 숨김)

### struct Combo
  (메서드 없음)

### class ZoneWiring
  - void GenRandom() → `RunGen` 위임 [wrapper]
  - void Gen12345() → `RunGen` 위임 [wrapper]
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/Map/Minimap

### class MinimapController : MonoBehaviour
  - byte[] GetExploredBits()
  - void MergeExploredBits(byte[] bits)
  - (내부 메서드 25개 숨김)

### enum MinimapMarkerType
  (메서드 없음)

### class MinimapMarker : MonoBehaviour
  - void OnEnable() → `_all.Add` 위임 [wrapper]
  - void OnDisable() → `_all.Remove` 위임 [wrapper]

### class MinimapNetworkSync : NetworkBehaviour
  - void Awake() → `GetComponent<MinimapController>` 위임 [wrapper]
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - (내부 메서드 4개 숨김)

## Assets/1.Scripts/Monster

### enum AoeTelegraphShape
  (메서드 없음)

### class AoeTelegraph : MonoBehaviour
  - void Show(float radius, float duration)
  - void Hide()
  - (내부 메서드 3개 숨김)

### interface IDeathEffect
  - void Play(Action onComplete)

### class DissolveDeath : MonoBehaviour, IDeathEffect
  - void Play(Action onComplete)
  - IEnumerator DelayThenComplete(Action onComplete) → `onComplete?.Invoke` 위임 [wrapper?]
  - void ApplyValue(float v) → `r.sharedMaterial.HasProperty` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### interface IMonsterStatusFacade
  - void ApplyStatus(StatusEffectType type, float duration)
  - void RemoveStatus(StatusEffectType type)
  - void ClearAll()

### class MonsterAnimationEventRelay : MonoBehaviour
  - void Awake() → `GetComponentInParent<MonsterBase>` 위임 [wrapper?]
  - void OnAttackHit()
  - void OnAttackCommit()
  - void OnAttackEnd()

### enum MonsterArchetype
  (메서드 없음)

### class MonsterBase : Unit
  - void OnNetworkSpawn()
  - void OnNetworkDespawn() → `base.OnNetworkDespawn` 위임 [wrapper?]
  - bool IsTargetValid(Transform t) → `MonsterTargeting.IsAttackable` 위임 [wrapper]
  - void NotifyAttackHit()
  - void NotifyAttackEnd()
  - void NotifyAttackCommit()
  - bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
  - void PlayHitVFXRpc(Vector3 sourcePosition) → `HitVFXPlayback.Play` 위임 [wrapper?]
  - void TakeDamage(AttackInfo attackInfo)
  - IEnumerator DespawnAfter(float delay) → `DespawnNow` 위임 [wrapper?]
  - bool CooldownReady() → `Mathf.Max` 위임 [wrapper]
  - (내부 메서드 50개 숨김)

### class MonsterDataSO : ScriptableObject
  (메서드 없음)

### class MonsterDeathEvents
  - void RaiseServerMonsterDied(Unit unit) → `ServerMonsterDied?.Invoke` 위임 [wrapper?]

### class MonsterMeleeAttack : BaseAttack
  - void BeginHitWindow() → `_windowHits.Clear` 위임 [wrapper?]
  - void EndHitWindow() → `_windowHits.Clear` 위임 [wrapper?]
  - void Awake() → `Mathf.Max` 위임 [wrapper?]
  - void Hit()
  - (내부 메서드 1개 숨김)

### class MonsterProjectile : NetworkBehaviour
  - void Launch(Unit owner, Vector3 direction, float speed, int damage, LayerMask targetLayer, float lifetime)
  - void LaunchBallistic(Unit owner, Vector3 initialVelocity, int damage, LayerMask targetLayer, float lifetime, float splashRadius)
  - (내부 메서드 5개 숨김)

### class MonsterRangedAttack : BaseAttack
  - void Awake() → `GetComponentInParent<Unit>` 위임 [wrapper?]
  - void ConfigureProjectile(GameObject prefab, float speed, float lifetime, float arcHeight, float splashRadius)
  - void Fire(Vector3 targetPoint)

### class MonsterSpawner : NetworkBehaviour
  - void OnNetworkSpawn()
  - int SpawnWave()
  - int SpawnAt(MonsterSpawnPoint point)
  - NetworkObject SpawnOne(GameObject prefab, Vector3 position, Quaternion rotation)
  - int CountAlive() → `_alive.RemoveAll` 위임 [wrapper?]
  - void OnNetworkDespawn()

### class MonsterSpawnPoint : MonoBehaviour
  - Vector3 GetSpawnPosition(int index)
  - (내부 메서드 1개 숨김)

### enum MonsterState
  (메서드 없음)

### class MonsterStatusEffect : NetworkBehaviour, IMonsterStatusFacade, IStatusEffectFacade
  - float GetStatMultiplier(StatusEffectType statType)
  - void ApplyStatus(StatusEffectType type, float duration)
  - void RemoveStatus(StatusEffectType type)
  - void ClearAll()
  - (내부 메서드 2개 숨김)

### class MonsterTargeting
  - bool IsAttackable(Transform target)
  - bool IsAttackable(Collider target) → `IsAttackable` 위임 [wrapper]

### class MonsterTestBootstrap : MonoBehaviour
  - (내부 메서드 6개 숨김)

## Assets/1.Scripts/Monster/Boss

### class BossBase : Unit
  - void OnNetworkSpawn()
  - void OnNetworkDespawn() → `base.OnNetworkDespawn` 위임 [wrapper?]
  - void TakeDamage(AttackInfo attackInfo)
  - IEnumerator DespawnAfter(float delay) → `DespawnNow` 위임 [wrapper?]
  - bool CooldownReady() → `Mathf.Max` 위임 [wrapper]
  - bool IsTargetValid(Transform t) → `MonsterTargeting.IsAttackable` 위임 [wrapper]
  - void OnStateChanged(BossState previous, BossState next) → `PlayStateAnimation` 위임 [wrapper?]
  - (내부 메서드 36개 숨김)

### class BossBasicAttackChoice : BaseAttackChoice
  - void Awake() → `attackChoices.Add` 위임 [wrapper?]
  - void AddType(T type)
  - void RemoveType(T type)
  - void PageEvent(int page)
  - int GetRandomAttack(float currentDistance)

### enum BossBasicAttackType
  (메서드 없음)

### enum BossState
  (메서드 없음)

### class GauntletBot : MonsterBase
  - void PlayAttackAnimClientRpc(GauntletAttackId attackId) → `SafeCrossFade` 위임 [wrapper?]
  - (내부 메서드 10개 숨김)

### enum GauntletAttackId
  (메서드 없음)

### class SpinnerBot : MonsterBase
  - void PlaySpinStartClientRpc() → `SafeCrossFade` 위임 [wrapper]
  - void PlaySpinLoopClientRpc() → `SafeCrossFade` 위임 [wrapper]
  - void PlayWhipClientRpc(bool useR) → `SafeCrossFade` 위임 [wrapper]
  - void PlayDizzyClientRpc(bool on) → `SafeSetBool` 위임 [wrapper]
  - (내부 메서드 5개 숨김)

## Assets/1.Scripts/Network

### class BaseNetworkBehaviour : NetworkBehaviour
  (메서드 없음)

### class ForProfile : MonoBehaviour
  - void Start() → `SubscribeToServerStarted` 위임 [wrapper?]
  - void OnDisable() → `UnsubscribeFromServerStarted` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### class NetworkClock : MonoBehaviour
  - void MarkMainGameStart()
  - void Pause()
  - void Resume()
  - (내부 메서드 11개 숨김)

### class NetworkSessionLauncher : MonoBehaviour
  - bool StartHost() → `StartHostCore` 위임 [wrapper?]
  - bool StartClient() → `StartClientCore` 위임 [wrapper?]
  - bool StartServer() → `StartServerCore` 위임 [wrapper?]
  - void OnSetConnectionData(string ip) → `SetDirectConnectionData` 위임 [wrapper?]
  - void OnSetConnectionData(string ip, ushort port) → `SetDirectConnectionData` 위임 [wrapper?]
  - Task<SessionStartResult> StartHostAsync(CancellationToken cancellationToken)
  - Task<SessionStartResult> StartClientAsync(string joinInput, CancellationToken cancellationToken)
  - void BeginHost() → `CompleteSessionStartAsync` 위임 [wrapper?]
  - void BeginClient(string joinInput) → `CompleteSessionStartAsync` 위임 [wrapper?]
  - void StartGameLoading()
  - (내부 메서드 11개 숨김)

### class UnityServicesBootstrap
  - void BeginInitialization() → `InitializeAsync` 위임 [wrapper?]
  - bool IsAvailable(string unavailableReason)
  - (내부 메서드 3개 숨김)

## Assets/1.Scripts/Network/Session

### class DirectIPv4ConnectionProvider : ISessionConnectionProvider
  - bool IsAvailable(string unavailableReason)
  - Task<SessionStartResult> PrepareHostAsync(CancellationToken cancellationToken)
  - Task<SessionStartResult> PrepareClientAsync(string joinInput, CancellationToken cancellationToken)
  - void SetConnectionData(string address, ushort port) → `ApplyConnectionData` 위임 [wrapper?]
  - (내부 메서드 3개 숨김)

### interface ISessionConnectionProvider
  - bool IsAvailable(string unavailableReason)
  - Task<SessionStartResult> PrepareHostAsync(CancellationToken cancellationToken)
  - Task<SessionStartResult> PrepareClientAsync(string joinInput, CancellationToken cancellationToken)

### class RelayConnectionProvider : ISessionConnectionProvider
  - bool IsAvailable(string unavailableReason)
  - Task<SessionStartResult> PrepareHostAsync(CancellationToken cancellationToken)
  - Task<SessionStartResult> PrepareClientAsync(string joinInput, CancellationToken cancellationToken)
  - (내부 메서드 1개 숨김)

### enum SessionConnectionMode
  (메서드 없음)

### struct SessionStartResult
  - SessionStartResult Succeeded(string shareCode)
  - SessionStartResult Failed(string failureReason)

## Assets/1.Scripts/Player

### class CharacterDefinition : ScriptableObject
  (메서드 없음)

### enum DefaultAttackChainPolicy
  (메서드 없음)

### enum DefaultAttackMovementType
  (메서드 없음)

### enum DefaultAttackRotationType
  (메서드 없음)

### enum DefaultAttackAnimationEventType
  (메서드 없음)

### enum DefaultAttackHitType
  (메서드 없음)

### class DefaultAttackController : BaseNetworkBehaviour
  - int GetAttackStateHash(int index) → `Animator.StringToHash` 위임 [wrapper?]
  - bool HasComboWindowOpenEvent(AnimationClip clip) → `nameof` 위임 [wrapper?]
  - bool TryStart()
  - void BeginFromState()
  - void ApplyData(DefaultAttackData data)
  - void SetAnimator(Animator newAnimator)
  - void Tick()
  - void CancelCurrentAttack()
  - void HandleAnimationEvent(DefaultAttackAnimationEventType eventType)
  - void EndCurrentAttack()
  - void HitCurrentAttack()
  - void HandleAnimatorMove(Vector3 deltaPosition, Vector3 animatorForward)
  - (내부 메서드 22개 숨김)

### class DefaultAttackStep
  (메서드 없음)

### class DefaultAttackData : ScriptableObject
  (메서드 없음)

### class DefaultAttackProjectile : BaseAttack
  - void Launch(Unit owner, Vector3 direction, float speed, int damage, LayerMask targetLayer)
  - (내부 메서드 2개 숨김)

### class PlayableCharacterVisual : MonoBehaviour
  - void ApplyCharacter(CharacterDefinition definition)
  - void ReplaceVisual(GameObject visualPrefab)
  - void BindExistingVisual() → `BindVisual` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### class Player : Unit
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - void OnGainedOwnership() → `ConfigureMovementPhysicsAuthority` 위임 [wrapper?]
  - void OnLostOwnership() → `ConfigureMovementPhysicsAuthority` 위임 [wrapper?]
  - void OnDestroy()
  - void EndDefaultAttack() → `defaultAttack.EndCurrentAttack` 위임 [wrapper?]
  - void HitDefaultAttack() → `defaultAttack.HitCurrentAttack` 위임 [wrapper?]
  - void HandleDefaultAttackEvent(DefaultAttackAnimationEventType eventType) → `defaultAttack.HandleAnimationEvent` 위임 [wrapper?]
  - void EndInterrupt() → `stateController.EndInterrupt` 위임 [wrapper?]
  - bool BeginAttackState() → `stateController.ChangeState` 위임 [wrapper?]
  - bool EndAttackState()
  - bool BeginRestrainedByInstigator(GameObject instigator, RestraintMode mode, float frontOffset)
  - bool EndRestrainedByInstigator()
  - bool BeginGrabbedByInstigator(GameObject instigator) → `BeginRestrainedByInstigator` 위임 [wrapper·주석확인]
  - bool EndGrabbedByInstigator() → `EndRestrainedByInstigator` 위임 [wrapper·주석확인]
  - void NotifyKnockbackEnded()
  - void NotifyKnockbackEndedServerRpc() → `stateController.EndKnockback` 위임 [wrapper?]
  - void SetAnimatorMoving(bool isMoving)
  - void TakeDamage(AttackInfo attackInfo) → `base.TakeDamage` 위임 [wrapper?]
  - bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
  - void ApplyFallDamage(float ratio)
  - (내부 메서드 14개 숨김)

### class PlayerAimIndicator : NetworkBehaviour
  - void OnNetworkSpawn()
  - (내부 메서드 4개 숨김)

### class PlayerAnimationEventRelay : MonoBehaviour
  - void EndDefaultAttack() → `HandleDefaultAttackEvent` 위임 [wrapper?]
  - void HitDefaultAttack() → `HandleDefaultAttackEvent` 위임 [wrapper?]
  - void HandleDefaultAttackEvent(int eventType)
  - void EndInterrupt()
  - void HandleSkillEvent(int eventType)
  - (내부 메서드 1개 숨김)

### class PlayerAudioListenerActivator : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - (내부 메서드 2개 숨김)

### class PlayerColorAssigner : NetworkBehaviour
  - void OnNetworkSpawn()

### struct DashMotionSettings
  (메서드 없음)

### class PlayerDashController : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - bool TryBeginPredictedDash()
  - void ServerResetChargeToOne()
  - void OwnerResetChargeToOne()
  - (내부 메서드 14개 숨김)

### class PlayerDefaultAttack : BaseAttack
  - void Awake() → `SetAttackType` 위임 [wrapper?]
  - void Configure(ColliderInfo defaultHitbox, LayerMask hittableLayers, int maxHitResults)
  - void PrepareStep(DefaultAttackStep step, int damageSnapshot, Vector3 direction)
  - void HitCurrentStep()
  - (내부 메서드 5개 숨김)

### class PlayerEncounterLock : NetworkBehaviour
  - void Awake() → `ResolveReferences` 위임 [wrapper?]
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - bool BeginCinematicServer()
  - bool EndCinematicServer()
  - void HandleLockChanged(bool previous, bool current) → `ApplyLocalLock` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### class PlayerGameRuleData : ScriptableObject
  (메서드 없음)

### class PlayerGroundingSensor : NetworkBehaviour
  - void SetGroundingMode(GroundingMode mode)
  - void RefreshNow()
  - bool IsOwnCollider(Collider candidate) → `candidate.transform.IsChildOf` 위임 [wrapper?]
  - void OnValidate() → `Mathf.Max` 위임 [wrapper?]
  - (내부 메서드 10개 숨김)

### enum GroundingMode
  (메서드 없음)

### enum VerticalMotionState
  (메서드 없음)

### class PlayerInputReader : BaseNetworkBehaviour
  - bool GetSkillPressed(PlayerSkillSlot slot)
  - bool GetSkillHeld(PlayerSkillSlot slot)
  - void Start() → `RefreshControlState` 위임 [wrapper?]
  - void OnNetworkSpawn() → `RefreshControlState` 위임 [wrapper?]
  - void OnNetworkDespawn() → `base.OnNetworkDespawn` 위임 [wrapper?]
  - void SetInputEnabled(bool isEnabled) → `ApplyInputState` 위임 [wrapper?]
  - void SetUiInputSuppressed(bool suppressed) → `ApplyInputState` 위임 [wrapper?]
  - void SetCombatInputEnabled(bool isEnabled)
  - void RefreshControlState() → `SetLocalControl` 위임 [wrapper?]
  - (내부 메서드 6개 숨김)

### enum InvulnerabilityCause
  (메서드 없음)

### class PlayerInvulnerability : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn() → `base.OnNetworkDespawn` 위임 [wrapper?]
  - void AddServerToken(InvulnerabilityCause cause, double durationSeconds)
  - void RemoveServerToken(InvulnerabilityCause cause)
  - void SetOwnerPredicted(bool isInvulnerable)
  - void HandleServerInvulnerableChanged(bool previous, bool current) → `ApplyHurtboxState` 위임 [wrapper?]
  - (내부 메서드 6개 숨김)

### class PlayerLandingProtection : NetworkBehaviour
  - void Awake() → `ResolveReferences` 위임 [wrapper?]
  - void OnNetworkDespawn()
  - void BeginProtection(InvulnerabilityCause cause, float duration, bool applyStun)
  - (내부 메서드 7개 숨김)

### class PlayerMotionSweep
  - Vector3 Resolve(CapsuleCollider capsule, Vector3 desiredDelta, float maxWalkableAngle, LayerMask obstacleMask, float skin, int maxIterations, RaycastHit[] buffer)
  - (내부 메서드 1개 숨김)

### class PlayerMovement : MonoBehaviour
  - void AddCarryDelta(Vector3 delta)
  - void Update() → `Rotate` 위임 [wrapper?]
  - void RotateImmediately(Vector3 direction)
  - void RotateToward(Vector3 direction, float speed)
  - void MoveRoot(Vector3 deltaPosition) → `rb.MovePosition` 위임 [wrapper?]
  - Vector3 GetInputWorldDirection()
  - void MoveTowardsPoint(Vector3 worldTarget)
  - void SetArmature(Transform newArmature)
  - (내부 메서드 7개 숨김)

### class PlayerRootMotionRelay : MonoBehaviour
  - void Awake() → `GetComponentInParent<DefaultAttackController>` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### class PlayerStateController : MonoBehaviour, IRestraintReceiver
  - void Tick()
  - bool ShouldTickForNetwork(bool isOwner, bool hasStateAuthority)
  - bool ChangeState(PlayerActionState nextState, string callerMember, string callerFile, int callerLine)
  - string DescribeCaller(string callerMember, string callerFile, int callerLine) → `string.IsNullOrEmpty` 위임 [wrapper?]
  - bool TryReceiveRestraint(RestraintContext restraintContext)
  - bool ApplyRestrainedFromServer(GameObject instigator, RestraintMode mode, float frontOffset) → `ApplyRestrained` 위임 [wrapper?]
  - bool BeginRestrained(GameObject instigator, RestraintMode mode, float frontOffset) → `TryReceiveRestraint` 위임 [wrapper?]
  - bool EndRestrained()
  - bool BeginKnockback(Vector3 direction, float strength)
  - bool ApplyKnockbackFromServer(Vector3 direction, float strength) → `BeginKnockback` 위임 [wrapper?]
  - void EndKnockback()
  - void EndInterrupt()
  - bool BeginCinematic()
  - bool EndCinematic()
  - bool BeginDash(Vector3 planarDirection, float speed, float duration, DashMotionSettings motion)
  - void EndDash()
  - bool BeginSkill(PlayerSkillBase skill)
  - void EndSkill()
  - (내부 메서드 7개 숨김)

### enum RestraintMode : byte
  (메서드 없음)

### struct RestraintContext
  (메서드 없음)

### interface IRestraintReceiver
  - bool TryReceiveRestraint(RestraintContext context)
  - bool BeginRestrained(GameObject instigator, RestraintMode mode, float frontOffset)
  - bool EndRestrained()

### enum PlayerActionState
  (메서드 없음)

### class PlayerStateContext
  (메서드 없음)

### interface IPlayerState
  - void Enter(PlayerActionState previousState)
  - void Tick()
  - void Exit(PlayerActionState nextState)

### class PlayerStateBase : IPlayerState
  - void Enter(PlayerActionState previousState)
  - void Tick()
  - void Exit(PlayerActionState nextState)
  - (내부 메서드 2개 숨김)

### class PlayerIdleState : PlayerStateBase
  - void Enter(PlayerActionState previousState) → `Context.Player.SetAnimatorMoving` 위임 [wrapper?]
  - void Tick()

### class PlayerMoveState : PlayerStateBase
  - void Enter(PlayerActionState previousState) → `Context.Player.SetAnimatorMoving` 위임 [wrapper?]
  - void Tick()
  - void Exit(PlayerActionState nextState)

### class PlayerAttackState : PlayerStateBase
  - void Enter(PlayerActionState previousState) → `Context.DefaultAttack.BeginFromState` 위임 [wrapper?]
  - void Tick() → `Context.DefaultAttack.Tick` 위임 [wrapper?]
  - void Exit(PlayerActionState nextState)

### class PlayerInterruptState : PlayerStateBase
  - bool CanStart(PlayerStateContext context)
  - void Enter(PlayerActionState previousState)
  - void Tick()
  - (내부 메서드 1개 숨김)

### class PlayerLockedState : PlayerStateBase
  - void Enter(PlayerActionState previousState) → `Context.Player.SetAnimatorMoving` 위임 [wrapper?]

### class PlayerRestrainedState : PlayerStateBase
  - void Enter(PlayerActionState previousState)
  - void Exit(PlayerActionState nextState)
  - void Tick()
  - (내부 메서드 4개 숨김)

### class PlayerKnockbackState : PlayerStateBase
  - void Enter(PlayerActionState previousState)
  - void Tick()
  - void Exit(PlayerActionState nextState)
  - void EndAndNotifyServer(string reason, float elapsed, float speed) → `Context.Player.NotifyKnockbackEnded` 위임 [wrapper?]

### class PlayerDashState : PlayerStateBase
  - void Enter(PlayerActionState previousState)
  - void Tick()
  - void Exit(PlayerActionState nextState)
  - (내부 메서드 3개 숨김)

### class PlayerUiInputPolicy : MonoBehaviour
  - void Awake() → `ResolveReferences` 위임 [wrapper?]
  - void Start() → `ApplyCurrentState` 위임 [wrapper?]
  - void HandleBlockedChanged(bool blocked) → `ApplyBlockedState` 위임 [wrapper?]
  - void ApplyCurrentState() → `ApplyBlockedState` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### class WeaponTransformRelay : MonoBehaviour
  - (내부 메서드 1개 숨김)

### struct WeaponFollow
  (메서드 없음)

## Assets/1.Scripts/Player/Corpse

### class PlayerCorpseController : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - bool ResolvePermanentDeath(PlayerDeathCause deathCause)
  - bool RemoveCorpsePermanently()
  - void HandleCorpseVisibilityChanged(bool previous, bool current) → `ConfigurePhysics` 위임 [wrapper?]
  - (내부 메서드 12개 숨김)

## Assets/1.Scripts/Player/Dash

### class DashChargeLedger
  - void Advance(double now)
  - bool TryConsume(double now)
  - void ForceReset(int count, double now)
  - void SyncToAuthoritative(int count, uint epoch, uint revision, double now)
  - void ForceAdoptAuthoritative(int count, uint epoch, uint revision, double now, double remainingToReady)
  - (내부 메서드 2개 숨김)

### class DashLog
  - void Log(object message, Object context) → `Debug.Log` 위임 [wrapper?]
  - void LogWarning(object message, Object context) → `Debug.LogWarning` 위임 [wrapper?]

### enum DashRejectReason
  (메서드 없음)

### struct DashRuntimeConfig
  - DashRuntimeConfig Create(double dashSpeed, double dashDuration, int maxCharge, double rechargeDuration, int snapshotCapacity, double snapshotFreshnessTolerance)
  - bool IsPositiveFinite(double v) → `double.IsNaN` 위임 [wrapper]
  - bool IsNonNegativeFinite(double v) → `double.IsNaN` 위임 [wrapper]

### class DashSnapshotHistory
  - bool Push(DashStateSnapshot snapshot)
  - bool TrySelectAtOrBefore(double requestTime, double freshnessTolerance, DashStateSnapshot result)

### struct DashStateSnapshot
  (메서드 없음)

### class DashValidationPolicy
  - DashValidationResult Validate(bool dashEnabled, double dashDuration, double snapshotFreshnessTolerance, double serverNow, double serverRtt, bool rttAvailable, int authoritativeChargeCount, Request request, DashSnapshotHistory history, CurrentState current)
  - bool IsFinite(double value) → `double.IsNaN` 위임 [wrapper]

### struct Request
  (메서드 없음)

### struct CurrentState
  (메서드 없음)

### struct DashValidationResult
  - DashValidationResult Reject(DashRejectReason reason)
  - DashValidationResult Approve(double remainingServerDuration, bool interrupted)

### class PlayerDashData : ScriptableObject
  - DashRuntimeConfig CreateValidatedConfig() → `DashRuntimeConfig.Create` 위임 [wrapper?]

### struct DashServerResponse
  (메서드 없음)

### class PlayerDashValidationManager : MonoBehaviour
  - void RegisterPlayer(ulong networkObjectId, ulong ownerClientId, DashRuntimeConfig config, double now)
  - void DeregisterPlayer(ulong networkObjectId)
  - void ForceReset(ulong networkObjectId, int count, double now)
  - void CaptureSnapshot(ulong networkObjectId, double serverTime, bool grounded, bool dead, bool soul, bool crowdControlled, bool landingProtected)
  - DashServerResponse ValidateRequest(ulong networkObjectId, ulong senderClientId, uint requestId, double clientLocalTime, double directionX, double directionZ, double serverNow, double serverRtt, bool rttAvailable, bool currentDead, bool currentSoul, bool currentCrowdControlled)
  - (내부 메서드 8개 숨김)

### class PlayerEntry
  (메서드 없음)

## Assets/1.Scripts/Player/Dash/Tests/EditMode

### class DashChargeLedgerTests
  - void SequentialRecharge_FillsOneByOne()
  - void Advance_CatchesUpMultipleCharges_InOneCall()
  - void Consume_WhenRecharging_DoesNotResetInProgressTimer()
  - void Consume_WhenFull_StartsFreshTimer()
  - void Consume_WhenEmpty_Fails()
  - void ForceReset_SetsCount_BumpsEpoch_ResetsRevision()
  - void SyncToAuthoritative_IsIgnored_WhenOwnerRevisionIsAhead()
  - void ForceAdoptAuthoritative_RefundsPredictedConsume_IgnoringRevision()
  - void ForceAdoptAuthoritative_TransplantsRemainingTime_AndClampsOutOfRange()
  - void MaxChargeOne_IsFullWhenOne_AndRechargesAfterConsume()

### class DashRuntimeConfigTests
  - void ValidValues_Enabled_AndCopied()
  - void ZeroDashDuration_Disabled() → `Assert.IsFalse` 위임 [wrapper?]
  - void ZeroMaxCharge_Disabled_AndClampedToOne()
  - void NegativeRecharge_Disabled() → `Assert.IsFalse` 위임 [wrapper?]
  - void ZeroSnapshotCapacity_Disabled_AndClamped()
  - void NaNDashSpeed_Disabled() → `Assert.IsFalse` 위임 [wrapper?]
  - void ZeroFreshnessTolerance_IsValid()

### class DashSnapshotHistoryTests
  - void Empty_SelectFails() → `Assert.IsFalse` 위임 [wrapper?]
  - void SelectsNewestAtOrBeforeRequest()
  - void FutureOnly_SelectFails()
  - void RejectsWhenSelectedTooStale()
  - void RingOverflow_DropsOldest()
  - void OutOfOrderPush_Ignored()
  - (내부 메서드 1개 숨김)

### class DashValidationPolicyTests
  - DashValidationResult Validate(DashSnapshotHistory history, double serverNow, double serverRtt, bool rttAvailable, bool dashEnabled, DashValidationPolicy.Request? req, DashValidationPolicy.CurrentState current, int authoritativeCharge) → `DashValidationPolicy.Validate` 위임 [wrapper]
  - void ConfigDisabled_Rejected() → `Assert.AreEqual` 위임 [wrapper?]
  - void NaNDirection_RejectedAsInvalidPayload() → `Assert.AreEqual` 위임 [wrapper?]
  - void ZeroDirection_RejectedAsInvalidPayload() → `Assert.AreEqual` 위임 [wrapper?]
  - void RttUnavailable_Rejected() → `Assert.AreEqual` 위임 [wrapper?]
  - void Rtt100ms_SelectsServerSideStart_AndComputesRemaining()
  - void Rtt250ms_OneWay125ms()
  - void HighLatency_AlreadyEnded_ApprovedWithZeroRemaining()
  - void NoSnapshot_Rejected() → `Assert.AreEqual` 위임 [wrapper?]
  - void StaleSnapshot_Rejected()
  - void NotGroundedAtSnapshot_Rejected() → `Assert.AreEqual` 위임 [wrapper?]
  - void NoAuthoritativeCharge_Rejected() → `Assert.AreEqual` 위임 [wrapper?]
  - void SnapshotChargeZero_ButAuthoritativeHasCharge_Approved()
  - void AuthoritativeChargeZero_ButSnapshotHadCharge_Rejected()
  - void DeadBeforeGrounded_DeadReasonWins_ValidationOrder() → `Assert.AreEqual` 위임 [wrapper?]
  - void ValidPastButCurrentlyDead_ApprovedButInterrupted()
  - (내부 메서드 4개 숨김)

## Assets/1.Scripts/Player/Editor

### class PlayerEncounterLockAuthoring
  - void Repair()
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/Player/Fall

### class FallBoundarySettings : MonoBehaviour
  - (내부 메서드 2개 숨김)

### struct FallDeathContext
  (메서드 없음)

### class PlayerFallController : NetworkBehaviour
  - void Awake() → `GetComponent<PlayerEncounterLock>` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class PlayerFallRecovery : NetworkBehaviour
  - void Awake() → `ResolveReferences` 위임 [wrapper?]
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - void HandleServerFallDeath(FallDeathContext context) → `ReturnAfterFallDeathRpc` 위임 [wrapper?]
  - void BeginRecoveryRpc(Vector3 returnPoint, Vector3 fallPoint) → `StartCoroutine` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### class PlayerSafePointTracker : NetworkBehaviour
  - void OnNetworkSpawn()
  - Vector3 ResolveReturnPoint(Vector3 fallPoint)
  - (내부 메서드 6개 숨김)

## Assets/1.Scripts/Player/Life

### interface IPlayerDeathPresentationConsumer
  - bool TryBeginDeathPresentation()

### class PlayerLifeCycleController : NetworkBehaviour, IPlayerDeathPresentationConsumer
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - bool TryBeginDeathPresentation() → `TryBeginDeathPresentation` 위임 [wrapper?]
  - bool TryBeginDeathPresentation(PlayerDeathCause deathCause)
  - bool TryEnterSoul() → `TryTransition` 위임 [wrapper?]
  - bool TryCompleteRevive()
  - bool TryEnterPermanentDead() → `TryTransition` 위임 [wrapper?]
  - bool TryEnterResolvedDeathState(PlayerLifeState destinationState) → `TryEnterPermanentDead` 위임 [wrapper?]
  - (내부 메서드 16개 숨김)

### class PlayerLifeInputPolicy : MonoBehaviour
  - void Awake() → `ResolveReferences` 위임 [wrapper?]
  - void Start() → `ApplyCurrentAccess` 위임 [wrapper?]
  - void HandleGameplayAccessChanged(PlayerLifeGameplayAccess access) → `ApplyAccess` 위임 [wrapper?]
  - void HandleCinematicLockChanged(bool isLocked) → `ApplyCurrentAccess` 위임 [wrapper?]
  - (내부 메서드 6개 숨김)

### enum PlayerLifeState
  (메서드 없음)

### enum PlayerDeathCause
  (메서드 없음)

### struct PlayerLifeGameplayAccess
  - PlayerLifeGameplayAccess FromState(PlayerLifeState state)

### class PlayerReviveController : NetworkBehaviour
  - void Awake() → `ResolveGameRuleReference` 위임 [wrapper?]
  - void OnNetworkSpawn()
  - bool TryCompleteReviveOnServer()
  - void RequestDebugReviveRpc() → `TryCompleteReviveOnServer` 위임 [wrapper?]
  - (내부 메서드 5개 숨김)

### interface IPlayerLifeCountInitialValueProvider
  - int GetInitialLifeCount(ulong clientId)

### struct PlayerLifeCountEntry : INetworkSerializable, IEquatable<PlayerLifeCountEntry>
  - void NetworkSerialize(BufferSerializer<T> serializer) → `serializer.SerializeValue` 위임 [wrapper?]
  - bool Equals(PlayerLifeCountEntry other)
  - bool Equals(object obj) → `Equals` 위임 [wrapper?]
  - int GetHashCode()

### class Temp_MultiGameRule : NetworkBehaviour
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - bool TryGetLifeCount(ulong clientId, int lifeCount)
  - bool HasReviveAvailable(ulong clientId) → `TryGetLifeCount` 위임 [wrapper?]
  - bool TryRegisterClient(ulong clientId)
  - bool TryUnregisterClient(ulong clientId)
  - bool TryResolveDeathState(ulong clientId, PlayerLifeState destinationState)
  - bool TryConsumeLifeAfterAliveRevive(ulong clientId, int remainingLifeCount)
  - void HandleClientConnected(ulong clientId) → `TryRegisterClient` 위임 [wrapper?]
  - void HandleClientDisconnected(ulong clientId) → `TryUnregisterClient` 위임 [wrapper?]
  - void HandleLifeCountsChanged(NetworkListEvent<PlayerLifeCountEntry> changeEvent) → `LifeCountsChanged?.Invoke` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/Player/Skill

### class FirstMeleeInterruptSkill : PlayerInstantSkill
  - void OnServerStart(Vector3 direction, Unit target)
  - void OnClientPlay(Vector3 direction)
  - void OnTick()
  - void OnAnimationEvent(SkillAnimationEventType eventType)
  - (내부 메서드 1개 숨김)

### class FirstMeleeInterruptSkillData : PlayerSkillData
  (메서드 없음)

### class FirstMeleeMainSkill : PlayerHoldSkill
  - void Initialize(Player owner, PlayerSkillController controller)
  - void OnServerStart(Vector3 direction, Unit target)
  - void OnClientPlay(Vector3 direction)
  - void OnAimUpdated(Vector3 direction) → `Flatten` 위임 [wrapper?]
  - void OnTick()
  - void OnEnd(SkillEndReason reason)
  - (내부 메서드 4개 숨김)

### class FirstMeleeMainSkillData : PlayerSkillData
  (메서드 없음)

### class FirstMeleePassive : BaseNetworkBehaviour
  - void Awake() → `GetComponent<PlayerDefaultAttack>` 위임 [wrapper?]
  - void OnNetworkSpawn()
  - void NotifyOwnerHit()
  - (내부 메서드 4개 숨김)

### class FirstMeleeSubSkill : PlayerInstantSkill
  - void OnServerStart(Vector3 direction, Unit target)
  - void OnClientPlay(Vector3 direction)
  - (내부 메서드 1개 숨김)

### class FirstMeleeSubSkillData : PlayerSkillData
  (메서드 없음)

### class FirstMeleeUltimateSkill : PlayerChannelingSkill
  - bool CanUse(Vector3 direction, Unit target)
  - void OnServerStart(Vector3 direction, Unit target) → `base.OnServerStart` 위임 [wrapper?]
  - void OnClientPlay(Vector3 direction)
  - void OnTick()
  - void OnEnd(SkillEndReason reason) → `base.OnEnd` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class FirstMeleeUltimateSkillData : PlayerSkillData
  (메서드 없음)

### class PlayerChannelingSkill : PlayerSkillBase
  - void OnServerStart(Vector3 direction, Unit target)
  - void OnTick()
  - (내부 메서드 2개 숨김)

### class PlayerHoldSkill : PlayerSkillBase
  - void OnServerStart(Vector3 direction, Unit target)
  - void OnTick()
  - void OnReleased()
  - (내부 메서드 1개 숨김)

### class PlayerInstantSkill : PlayerSkillBase
  - void OnServerStart(Vector3 direction, Unit target)
  - void OnAnimationEvent(SkillAnimationEventType eventType)

### enum SkillState
  (메서드 없음)

### enum SkillAnimationEventType
  (메서드 없음)

### enum SkillEndReason
  (메서드 없음)

### class PlayerSkillBase : MonoBehaviour
  - void Initialize(Player owner, PlayerSkillController controller)
  - bool CanUse(Vector3 direction, Unit target)
  - void SetDamageSnapshot(int value) → `Mathf.Max` 위임 [wrapper?]
  - void SetAimPoint(Vector3 point, bool hasPoint)
  - void ResetToReady()
  - void OnServerStart(Vector3 direction, Unit target)
  - void OnClientPlay(Vector3 direction)
  - void OnTick()
  - void OnAnimationEvent(SkillAnimationEventType eventType)
  - void OnAimUpdated(Vector3 direction)
  - void OnReleased()
  - void OnEnd(SkillEndReason reason)
  - (내부 메서드 3개 숨김)

### class PlayerSkillController : BaseNetworkBehaviour
  - PlayerSkillBase GetSkill(PlayerSkillSlot slot)
  - bool IsCooldownReady(PlayerSkillSlot slot)
  - float GetCooldownRemaining(PlayerSkillSlot slot) → `Mathf.Max` 위임 [wrapper?]
  - bool TryUse(PlayerSkillSlot slot)
  - bool TryUse(PlayerSkillSlot slot, Unit target) → `Cast` 위임 [wrapper?]
  - bool ExecuteTargetedSkill(PlayerSkillSlot slot, Unit target, Vector3 aimPoint, bool hasAimPoint) → `Cast` 위임 [wrapper?]
  - bool WasSkillRePressed(PlayerSkillSlot slot) → `inputReader.GetSkillPressed` 위임 [wrapper?]
  - void Tick()
  - void HandleSkillStateExit(PlayerActionState nextState)
  - void EndActiveSkillServer(SkillEndReason reason)
  - void HandleAnimationEvent(SkillAnimationEventType eventType)
  - Unit ResolveTarget(NetworkObjectReference targetRef) → `targetRef.TryGet` 위임 [wrapper?]
  - (내부 메서드 19개 숨김)

### enum PlayerSkillInputType
  (메서드 없음)

### class PlayerSkillData : ScriptableObject
  (메서드 없음)

### enum PlayerSkillSlot
  (메서드 없음)

### class PlayerSkillState : PlayerStateBase
  - void Enter(PlayerActionState previousState)
  - void Tick() → `Context.Skills?.Tick` 위임 [wrapper?]
  - void Exit(PlayerActionState nextState) → `Context.Skills?.HandleSkillStateExit` 위임 [wrapper?]

## Assets/1.Scripts/Player/Skill/Targeting

### class PlayerSkillTargeting : MonoBehaviour
  - bool Begin(PlayerSkillSlot slot)
  - void Cancel() → `StopMoveToCast` 위임 [wrapper?]
  - (내부 메서드 18개 숨김)

### enum SkillConfirmMode
  (메서드 없음)

### enum SkillCursorState
  (메서드 없음)

### class SkillCursorView : MonoBehaviour
  - void ApplyState(SkillCursorState state)
  - CursorIcon Resolve(SkillCursorState state) → `FirstAssigned` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### struct CursorIcon
  (메서드 없음)

### class SkillRangeIndicator : MonoBehaviour
  - void Awake() → `HideAll` 위임 [wrapper?]
  - void ShowRange(float radius)
  - void SetGroundMarker(bool show, Vector3 worldPoint)
  - void HideAll()

### enum SkillTargetingMode
  (메서드 없음)

## Assets/1.Scripts/Player/Soul

### class PlayerSoulController : MonoBehaviour
  - void ApplyLifeState(PlayerLifeState state)
  - void SetCharacterDefinition(CharacterDefinition definition)
  - bool TryGetFixedMoveSpeed(float moveSpeed)
  - void HandleLifeStateChanged(PlayerLifeState previousState, PlayerLifeState currentState) → `ApplyLifeState` 위임 [wrapper?]
  - void HandleCharacterApplied(CharacterDefinition definition) → `SetCharacterDefinition` 위임 [wrapper?]
  - void OnValidate() → `Mathf.Max` 위임 [wrapper?]
  - (내부 메서드 20개 숨김)

## Assets/1.Scripts/Rendering

### class WallOcclusionDriver : MonoBehaviour
  - void SetSettings(WallOcclusionSettings newSettings)
  - void Rebind()
  - (내부 메서드 8개 숨김)

## Assets/1.Scripts/Rendering/Editor

### class WallOcclusionAuthoring
  - void ApplyAll()
  - void DumpShaderMessages()
  - void ValidateAll()
  - Dictionary<Material, Material> EnsureMaterials()
  - WallOcclusionSettings EnsureSettings(Dictionary<Material, Material> materialMap)
  - (내부 메서드 9개 숨김)

### class WallOcclusionTestRunner
  - (내부 메서드 1개 숨김)

### class Callbacks : ICallbacks
  - void RunStarted(ITestAdaptor testsToRun) → `Debug.Log` 위임 [wrapper?]
  - void RunFinished(ITestResultAdaptor result)
  - void TestStarted(ITestAdaptor test)
  - void TestFinished(ITestResultAdaptor result)

## Assets/1.Scripts/Rendering/Fog

### class FogManager : MonoBehaviour
  - void Register(FogVolume v)
  - void Unregister(FogVolume v) → `s_volumes.Remove` 위임 [wrapper]
  - void LateUpdate() → `PushGlobals` 위임 [wrapper]
  - void InvalidateLosNodes()
  - (내부 메서드 15개 숨김)

### enum FogDistanceMode
  (메서드 없음)

### class FogProfile : ScriptableObject
  (메서드 없음)

### class FogRendererFeature : ScriptableRendererFeature
  - void Create()
  - void AddRenderPasses(ScriptableRenderer renderer, RenderingData renderingData)
  - void Dispose(bool disposing) → `CoreUtils.Destroy` 위임 [wrapper?]

### class FogPass : ScriptableRenderPass
  - void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)

### class PassData
  (메서드 없음)

### enum FogVolumeShape
  (메서드 없음)

### class FogVolume : MonoBehaviour
  - void OnEnable() → `FogManager.Register` 위임 [wrapper]
  - void OnDisable() → `FogManager.Unregister` 위임 [wrapper]
  - Matrix4x4 GetWorldToLocal() → `Matrix4x4.TRS` 위임 [wrapper?]
  - Vector4 GetBounds()
  - Vector4 GetParams0()
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/Rendering/Fog/Editor

### class FogPainterWindow : EditorWindow
  - void Open() → `GetWindow<FogPainterWindow>` 위임 [wrapper]
  - void OnEnable() → `TryAutoFind` 위임 [wrapper?]
  - void OnDisable() → `FlushIfDirty` 위임 [wrapper?]
  - (내부 메서드 8개 숨김)

### enum BrushMode
  (메서드 없음)

### class FogVolumeEditor : Editor
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/Rendering/Occlusion

### class WallOcclusionGlobals
  - Vector4 BuildRange(WallOcclusionSettings settings, bool enabled)
  - Vector4 BuildShape(WallOcclusionSettings settings)
  - void Apply(WallOcclusionSettings settings, Vector3 cameraPosition, Vector3 playerPosition)
  - void Disable() → `Shader.SetGlobalVector` 위임 [wrapper?]

### struct WallOcclusionBindReport
  (메서드 없음)

### class WallOcclusionMaterialBinder
  - WallOcclusionBindReport Bind(WallOcclusionSettings settings, IEnumerable<Transform> roots)
  - string DescribeUnmapped(IReadOnlyCollection<string> names, int maxReported)
  - (내부 메서드 2개 숨김)

### class WallOcclusionSettings : ScriptableObject
  - bool IsExcludedByName(string objectName)
  - bool TryResolveOcclusionMaterial(Material current, Material variant)
  - void ConfigureMaterialMappings(Material[] sources, Material[] variants) → `Array.Empty<Material>` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/RuntimeSafety

### struct RuntimeSceneServiceReport
  (메서드 없음)

### class RuntimeSceneServiceCoordinator
  - RuntimeSceneServiceReport Reconcile()
  - void RestoreAll() → `RestoreSuppressed` 위임 [wrapper?]
  - int GetScenePriority(Scene scene) → `GetScenePriority` 위임 [wrapper?]
  - void RestoreSuppressed(HashSet<T> suppressed) → `suppressed.Clear` 위임 [wrapper?]
  - void HandleSceneChanged(Scene _, LoadSceneMode __) → `Reconcile` 위임 [wrapper?]
  - void HandleSceneUnloaded(Scene _) → `Reconcile` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### class UnreadableMeshColliderBakeScope : IDisposable
  - UnreadableMeshColliderBakeScope BeginLoadedScenes() → `Begin` 위임 [wrapper?]
  - UnreadableMeshColliderBakeScope Begin(IEnumerable<MeshCollider> colliders)
  - void Dispose()
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/Scene

### class KMKScene : NetworkBehaviour
  - void OnNetworkSpawn()

## Assets/1.Scripts/Sound

### class AudioManager : MonoBehaviour
  - IAudioPlayer PlayOneShot(SoundID id)
  - IAudioPlayer PlayOneShot(SoundID id, Vector3 worldPos)
  - void PlayBGM(SoundID id)
  - void StopBGM(float fadeOut) → `BroAudio.Stop` 위임 [wrapper?]
  - void InitVolumes() → `System.Enum.GetValues` 위임 [wrapper?]
  - void SetVolume(BroAudioType audioType, float volume, float fadeTime) → `BroAudio.SetVolume` 위임 [wrapper?]
  - void SetMasterVolume(float volume, float fadeTime) → `BroAudio.SetVolume` 위임 [wrapper?]
  - float GetVolume(BroAudioType audioType) → `_volumes.TryGetValue` 위임 [wrapper?]
  - void Pause(BroAudioType audioType) → `BroAudio.Pause` 위임 [wrapper?]
  - void UnPause(BroAudioType audioType) → `BroAudio.UnPause` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class SceneBgmSwitcher : MonoBehaviour
  - void Start() → `PlayForScene` 위임 [wrapper?]
  - void OnSceneLoaded(Scene scene, LoadSceneMode mode) → `PlayForScene` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### struct SceneBgm
  (메서드 없음)

### class VolumeSlider : MonoBehaviour
  - void Awake() → `GetComponent<Slider>` 위임 [wrapper?]
  - void OnDisable() → `_slider.onValueChanged.RemoveListener` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

## Assets/1.Scripts/Sound/Editor

### class VolumeUIBuilder
  - void Build()
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/UI

### class PersistentEventSystem : MonoBehaviour
  - void OnSceneLoaded(Scene scene, LoadSceneMode mode) → `RemoveForeignEventSystems` 위임 [wrapper?]
  - void RemoveForeignEventSystems() → `Destroy` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class ResultStatsView : MonoBehaviour
  - void Start() → `Apply` 위임 [wrapper?]
  - void Apply()
  - TMP_Text FindText(string childName) → `GetComponentsInChildren<TMP_Text>` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class UiModalBlocker : MonoBehaviour
  - void OnEnable() → `UiInputGateManager.Acquire` 위임 [wrapper?]
  - void OnDisable() → `UiInputGateManager.Release` 위임 [wrapper?]

## Assets/1.Scripts/UI/Combat

### class BossHealthHUD : MonoBehaviour
  - void OnDisable() → `BindBoss` 위임 [wrapper?]
  - (내부 메서드 3개 숨김)

### class BossHudTarget : BaseNetworkBehaviour
  - void Awake() → `GetComponent<Unit>` 위임 [wrapper?]
  - void OnNetworkSpawn()
  - void OnNetworkDespawn() → `base.OnNetworkDespawn` 위임 [wrapper?]
  - void OnDestroy() → `base.OnDestroy` 위임 [wrapper?]

### class CombatHUD : MonoBehaviour
  - void OnEnable() → `Bind` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class DashCooldownHUD : MonoBehaviour, ICombatUiBlockedStateView
  - void Awake() → `CacheSlotColors` 위임 [wrapper?]
  - void Bind(Player player) → `Refresh` 위임 [wrapper?]
  - void SetBlocked(bool blocked)
  - void Update() → `Refresh` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class DelayedHealthBar
  - void Bind(int hp)
  - void OnHpChanged(int previous, int next)
  - void Tick(float deltaTime, int hp, int maxHp)

### class PassiveHUD : MonoBehaviour
  - void Bind(Player player) → `Refresh` 위임 [wrapper?]
  - void Update() → `Refresh` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### interface ICombatUiBlockedStateView
  - void SetBlocked(bool blocked)

### class PlayerCombatUiLifecyclePolicy : MonoBehaviour
  - void Awake() → `CacheViews` 위임 [wrapper?]
  - void OnEnable() → `Bind` 위임 [wrapper?]
  - void OnDisable() → `UnbindLifeCycle` 위임 [wrapper?]
  - void HandleLifeStateChanged(PlayerLifeState previousState, PlayerLifeState currentState) → `ApplyState` 위임 [wrapper?]
  - PlayerLifeCycleController ResolveLifeCycle(Player player) → `player.GetComponent<PlayerLifeCycleController>` 위임 [wrapper?]
  - (내부 메서드 5개 숨김)

### class PlayerHealthHUD : MonoBehaviour
  - void Bind(Player boundPlayer)
  - void SetDisplayOverrideZero(bool shouldOverride)
  - (내부 메서드 4개 숨김)

### class SkillCooldownHUD : MonoBehaviour, ICombatUiBlockedStateView
  - void Awake() → `CacheSlotColors` 위임 [wrapper?]
  - void Bind(Player player) → `Refresh` 위임 [wrapper?]
  - void SetBlocked(bool blocked)
  - void Update() → `Refresh` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class SlotWidget
  (메서드 없음)

### class StatusEffectHUD : MonoBehaviour
  - void Bind(Player player) → `Refresh` 위임 [wrapper?]
  - void Update() → `Refresh` 위임 [wrapper?]
  - (내부 메서드 2개 숨김)

### class EffectWidget
  (메서드 없음)

### class UnitOverheadHealthBar : MonoBehaviour
  - void Awake() → `GetComponentInParent<Player>` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/UI/Combat/FloatingDamage

### class FloatingDamageAnchor : MonoBehaviour
  (메서드 없음)

### class FloatingDamagePopup : MonoBehaviour
  - void Initialize(FloatingPopupRequest request, FloatingDamageSettings settings, FloatingPopupStyle style, Action<FloatingDamagePopup> release)
  - bool TryAccumulate(int amount, bool fromLocalPlayer)
  - void ForceRelease() → `RequestRelease` 위임 [wrapper?]
  - (내부 메서드 10개 숨김)

### enum PopupState
  (메서드 없음)

### class FloatingDamagePresenter : MonoBehaviour
  - void Awake() → `GetComponent<Unit>` 위임 [wrapper?]
  - (내부 메서드 7개 숨김)

### enum DamageChannel
  (메서드 없음)

### enum PopupKind
  (메서드 없음)

### enum FloatingDamageDisplayFilter
  (메서드 없음)

### struct FloatingPopupRequest
  (메서드 없음)

### struct FloatingPopupStyle
  (메서드 없음)

### class FloatingDamageSettings : ScriptableObject
  - bool TryGetStyle(PopupKind kind, FloatingPopupStyle style)

### class FloatingDamageSpawner : MonoBehaviour
  - void Submit(FloatingPopupRequest request)
  - (내부 메서드 6개 숨김)

### struct PopupKey
  - bool Equals(object obj)
  - int GetHashCode()

## Assets/1.Scripts/Unit

### class Health
  - void TakeHpDamage(int damage)
  - void HealHp(int healAmount)
  - void Revive()
  - void DecreaseDefense(int decreaseAmount)
  - void IncreaseDefense(int increaseAmount)
  - void TakeShieldDamage(int damage)
  - void SetShield(int shieldValue)
  - void IncreaseShield(int shieldAmount)

### class HitFlash : MonoBehaviour
  - void Awake() → `GetComponent<Unit>` 위임 [wrapper?]
  - (내부 메서드 7개 숨김)

### class Hurtbox : MonoBehaviour
  - void Awake() → `ResolveOwner` 위임 [wrapper?]
  - void OnValidate() → `ResolveOwner` 위임 [wrapper?]
  - bool TryGetOwner(Unit unit)
  - bool TryGetReceiver(IAttackReceiver receiver)
  - bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
  - void ResolveOwner() → `ResolveReferences` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### interface IStatusEffectFacade
  - float GetStatMultiplier(StatusEffectType statType)

### struct StatusEffectInstance : INetworkSerializable, IEquatable<StatusEffectInstance>
  - void NetworkSerialize(BufferSerializer<T> serializer)
  - bool Equals(StatusEffectInstance other)

### class StatusEffectController : BaseNetworkBehaviour, IStatusEffectFacade
  - void Awake() → `GetComponent<PlayerEncounterLock>` 위임 [wrapper?]
  - bool Has(StatusEffectType type)
  - float GetStatMultiplier(StatusEffectType statType)
  - int GetStackCount(StatusEffectType type, ulong sourceId) → `Mathf.Max` 위임 [wrapper?]
  - StatusEffectInstance GetActive(int index)
  - float GetRemainingTime(int index)
  - void Apply(StatusEffectType type, float duration, ulong sourceId) → `Apply` 위임 [wrapper?]
  - void Apply(StatusEffectType type, float magnitude, float duration, ulong sourceId, int maxStacks)
  - bool Remove(StatusEffectType type, ulong sourceId)
  - int RemoveBySource(ulong sourceId)
  - int ClearAllServer()
  - (내부 메서드 3개 숨김)

### enum StatusEffectType
  (메서드 없음)

### class Unit : BaseNetworkBehaviour, IAttackReceiver
  - void ChangeAttackDamageValue(int newAttackDamage)
  - void TakeDamage(int damage) → `ApplyHealthDamage` 위임 [wrapper?]
  - void TakeDamage(AttackInfo attackInfo) → `TakeDamage` 위임 [wrapper?]
  - bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
  - void ApplyDirectHealthDamage(int damage) → `ApplyHealthDamage` 위임 [wrapper?]
  - void ApplyMaxHealthPercentDamage(float ratio) → `ApplyHealthDamage` 위임 [wrapper?]
  - void ApplyCurrentHealthPercentDamage(float ratio) → `ApplyHealthDamage` 위임 [wrapper?]
  - void BreakShield()
  - void HealHp(int healAmount)
  - void Revive()
  - void IncreaseDefense(int increaseAmount)
  - void DecreaseDefense(int decreaseAmount)
  - void IncreaseShield(int shieldAmount)
  - void SetShield(int shieldValue)
  - void ChangeMoveSpeedValue(float newMoveSpeed)
  - void ChangeAttackSpeedValue(float newAttackSpeed)
  - float GetStatMultiplier(StatusEffectType statType) → `StatusFacade.GetStatMultiplier` 위임 [wrapper?]
  - void ChangeAttackDamageValueRpc(int newAttackDamage, RpcParams rpcParams)
  - void HealHpRpc(int healAmount, RpcParams rpcParams)
  - void IncreaseDefenseRpc(int increaseAmount, RpcParams rpcParams)
  - void DecreaseDefenseRpc(int decreaseAmount, RpcParams rpcParams)
  - void IncreaseShieldRpc(int shieldAmount, RpcParams rpcParams)
  - void SetShieldRpc(int shieldValue, RpcParams rpcParams)
  - void ChangeMoveSpeedValueRpc(float newMoveSpeed, RpcParams rpcParams)
  - void ChangeAttackSpeedValueRpc(float newAttackSpeed, RpcParams rpcParams)
  - void Initialize(int attackDamage, float moveSpeed, float attackSpeed, int maxHp, int defense)
  - void OnNetworkSpawn()
  - void OnNetworkDespawn()
  - void Knockback(Vector3 direction, float strength)
  - (내부 메서드 10개 숨김)

## Assets/1.Scripts/Unit/Weapon

### enum AttackElement
  (메서드 없음)

### class AttackTriggerRelay : NetworkBehaviour
  - (내부 메서드 3개 숨김)

### enum AttackType
  (메서드 없음)

### struct AttackInfo
  (메서드 없음)

### struct AttackHitContext
  (메서드 없음)

### class BaseAttack : MonoBehaviour, IDamageSettable
  - void SetDamageSnapshot(int value) → `InitializeAttackInfo` 위임 [wrapper?]
  - void SetDamage(int value) → `Mathf.Max` 위임 [wrapper?]
  - void SetTargetLayer(LayerMask value)
  - void SetAttackType(AttackType value) → `InitializeAttackInfo` 위임 [wrapper?]
  - string GetTargetName(Hurtbox hurtbox) → `hurtbox.TryGetOwner` 위임 [wrapper?]
  - (내부 메서드 11개 숨김)

### interface IAttackReceiver
  - bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)

### interface IDamageSettable
  - void SetDamage(int value)

### interface IKnockbackable
  - void ApplyKnockback(Vector3 direction, float strength)

### interface IKnockbackSettable
  - void SetKnockbackStrength(float value)

### class LinearKnockback : NetworkBehaviour, IKnockbackable
  - void ApplyKnockback(Vector3 direction, float strength)
  - void ApplyKnockbackClientRpc(Vector3 direction, float strength, ClientRpcParams rpcParams) → `_rigidbody.AddForce` 위임 [wrapper?]
  - (내부 메서드 4개 숨김)

### class OverlapAttack : BaseAttack
  - void Awake() → `Mathf.Max` 위임 [wrapper?]
  - void Hit()
  - (내부 메서드 1개 숨김)

## Assets/1.Scripts/Utility

### class AnimClipUtility
  - float GetPlayTime(Animator animator, string animClip, string multiplier, float clipStart, float clipEnd)

### class BitMaskHelper
  - T Add(T original, T newState)
  - T Remove(T original, T newState)
  - bool CheckEquals(T original, T newState) → `EqualityComparer<T>.Default.Equals` 위임 [wrapper?]
  - bool CheckContains(T original, T newState)

### enum OverlapCollider
  (메서드 없음)

### struct BoxColliderInfo
  (메서드 없음)

### struct CapsuleColliderInfo
  (메서드 없음)

### struct SphereColliderInfo
  (메서드 없음)

### class ColliderInfo : MonoBehaviour
  - void GetBoxColliderInfo(BoxColliderInfo info)
  - void GetCapsuleColliderInfo(CapsuleColliderInfo info)
  - void GetSphereColliderInfo(SphereColliderInfo info)
  - (내부 메서드 2개 숨김)

### class Edit
  - void Log(object message, Object context) → `Debug.Log` 위임 [wrapper?]
  - void LogWarning(object message, Object context) → `Debug.LogWarning` 위임 [wrapper?]
  - void LogError(object message, Object context) → `Debug.LogError` 위임 [wrapper?]
  - void LogAssertion(object message, Object context) → `Debug.LogAssertion` 위임 [wrapper?]

### class EnableCollider : NetworkBehaviour
  - void OnNetworkSpawn()
  - void SetEnableCollider(bool enable)
  - (내부 메서드 2개 숨김)

### class GroundProbe
  - float SurfaceY(RaycastHit ground)
  - bool TryFindGround(Vector3 point, int extraMask, RaycastHit ground, string report)

### class SpawnPointer : MonoBehaviour
  - void SetSpawnPoint(Vector3 point)

## Assets/1.Scripts/Utility/Editor

### class UnityMcpBehaviorGraphTools
  - object ListBehaviorGraphs(string argsJson)
  - object GetBehaviorGraph(string argsJson)
  - object OpenBehaviorGraph(string argsJson)
  - object SetBehaviorGraphDescription(string argsJson)
  - object SetBehaviorNodePosition(string argsJson)
  - object SetBehaviorBlackboardVariableValue(string argsJson)
  - SerializedProperty FindVariablesProperty(SerializedObject serializedObject) → `serializedObject.FindProperty` 위임 [wrapper?]
  - UnityEngine.Object FindAssetAtPath(string path, string fullTypeName, string objectName) → `AssetDatabase.LoadAllAssetsAtPath` 위임 [wrapper?]
  - (내부 메서드 21개 숨김)

### class ListBehaviorGraphsArgs
  (메서드 없음)

### class GetBehaviorGraphArgs
  (메서드 없음)

### class OpenBehaviorGraphArgs
  (메서드 없음)

### class SetBehaviorGraphDescriptionArgs
  (메서드 없음)

### class SetBehaviorNodePositionArgs
  (메서드 없음)

### class SetBehaviorBlackboardVariableValueArgs
  (메서드 없음)

### class ErrorResult
  (메서드 없음)

### class ListBehaviorGraphsResult
  (메서드 없음)

### class BehaviorAssetInfo
  (메서드 없음)

### class BehaviorGraphInfo
  (메서드 없음)

### class BehaviorNodeInfo
  (메서드 없음)

### class BehaviorFieldInfo
  (메서드 없음)

### class BehaviorEdgeInfo
  (메서드 없음)

### class BehaviorBlackboardVariableInfo
  (메서드 없음)

### class BehaviorNodeModelInfo
  (메서드 없음)

### class OpenBehaviorGraphResult
  (메서드 없음)

### class SetBehaviorGraphDescriptionResult
  (메서드 없음)

### class SetBehaviorNodePositionResult
  (메서드 없음)

### class SetBehaviorBlackboardVariableValueResult
  (메서드 없음)

## Assets/1.Scripts/Utility/Math

### class ColliderMathUtility
  - Vector3 Abs(Vector3 value) → `Mathf.Abs` 위임 [wrapper?]
  - Vector3 GetCapsuleLocalAxis(int direction)
  - float GetAxisScale(Vector3 scale, int direction)
  - float GetCapsuleRadiusScale(Vector3 scale, int direction)

## Assets/9.ScriptableObject/Enemy/Boss/Wells&No.23

### class TwentyThreeBasicAttackFigure : ScriptableObject
  (메서드 없음)

### enum TwentyThreeDamageType
  (메서드 없음)

### class TwentyThreeWells_Figure : ScriptableObject
  - int GetDamage(TwentyThreeDamageType type)
  - float GetKnockbackStrength(TwentyThreeDamageType type)

## Assets/9.ScriptableObject/Sound

### class SoundCatalog : ScriptableObject
  (메서드 없음)

## Assets/INab Studio/Demo Assets/Unity Companion License/StarterAssets/FirstPersonController/Scripts

### class BasicRigidBodyPush : MonoBehaviour
  - (내부 메서드 2개 숨김)

### class FirstPersonController : MonoBehaviour
  - void LateUpdate() → `CameraRotation` 위임 [wrapper?]
  - (내부 메서드 9개 숨김)

## Assets/INab Studio/Vfx Assets/Character Effects/Core/Scripts

### class CharacterEffect : UniformMeshSample
  - void PlayEffect_CharacterEffect() → `SendPlayEvent` 위임 [wrapper?]
  - void StopEffect_CharacterEffect() → `SendStopEvent` 위임 [wrapper?]

## Assets/INab Studio/Vfx Assets/Character Effects/Core/Scripts/Editor

### class CharacterEffectEditor : UniformMeshSampleEditor
  - void OnEnable()
  - void OnInspectorGUI() → `base.OnInspectorGUI` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

## Assets/INab Studio/Vfx Assets/Character Effects/Demo Files

### class CharacterEffectAPIShowcase : MonoBehaviour
  - void StartEffect()
  - void EndEffect()
  - void SetNewEffectPrefab(GameObject newPrefab)
  - void SetEffectPrefab1() → `StartEffect` 위임 [wrapper?]
  - void SetEffectPrefab2() → `StartEffect` 위임 [wrapper?]

### class ShowcaseSpawnerCharacterEffect : MonoBehaviour
  - void OnEnable() → `PlayAll` 위임 [wrapper?]
  - void SpawnPrefabs()
  - void DestroyPrefabs() → `spawnedObjects.Clear` 위임 [wrapper?]
  - void PlayAll() → `obj.GetComponentsInChildren<CharacterEffect>` 위임 [wrapper?]
  - void StopAll() → `obj.GetComponentsInChildren<CharacterEffect>` 위임 [wrapper?]

### class ShowcaseSpawnerCharacterEffectEditor : Editor
  - void OnInspectorGUI()

## Assets/INab Studio/Vfx Assets/Common/Editor

### class EditorUtilties
  - bool GetFoldoutState(string key, UnityEngine.Object gameObject) → `SessionState.GetBool` 위임 [wrapper?]
  - void SetFoldoutState(string key, UnityEngine.Object gameObject, bool value) → `SessionState.SetBool` 위임 [wrapper?]
  - bool FoldoutGeneral(UnityEngine.Object gameObject) → `GetFoldoutState` 위임 [wrapper?]
  - void SetFoldoutGeneral(UnityEngine.Object gameObject, bool value) → `SetFoldoutState` 위임 [wrapper?]
  - bool FoldoutEditorTesting(UnityEngine.Object gameObject) → `GetFoldoutState` 위임 [wrapper?]
  - void SetFoldoutEditorTesting(UnityEngine.Object gameObject, bool value) → `SetFoldoutState` 위임 [wrapper?]
  - bool FoldoutEffectSettings(UnityEngine.Object gameObject) → `GetFoldoutState` 위임 [wrapper?]
  - bool AnimatorEffectSettings(UnityEngine.Object gameObject) → `GetFoldoutState` 위임 [wrapper?]
  - void SetFoldoutEffectSettings(UnityEngine.Object gameObject, bool value) → `SetFoldoutState` 위임 [wrapper?]
  - void SetAnimatorEffectSettings(UnityEngine.Object gameObject, bool value) → `SetFoldoutState` 위임 [wrapper?]
  - bool FoldoutMaterialsProperties(UnityEngine.Object gameObject) → `GetFoldoutState` 위임 [wrapper?]
  - void SetFoldoutMaterialsProperties(UnityEngine.Object gameObject, bool value) → `SetFoldoutState` 위임 [wrapper?]

### struct LabeledSectionScope : IDisposable
  - void Dispose() → `EditorGUILayout.Space` 위임 [wrapper?]

### struct LabeledSectionScopeBox : IDisposable
  - void Dispose()

### struct FoldoutHeaderScope : IDisposable
  - void Dispose() → `EditorGUILayout.EndFoldoutHeaderGroup` 위임 [wrapper?]

## Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh

### struct BarycentricTriangleSampling
  (메서드 없음)

### class UniformMeshBaker
  - void Update(VisualEffect visualEffect, Renderer renderer)
  - void OnDisable()
  - void Bake(VisualEffect visualEffect, Renderer renderer)
  - void SetGraphicsBuffer(VisualEffect visualEffect) → `BindGraphicsBuffer` 위임 [wrapper?]
  - (내부 메서드 3개 숨김)

### class MeshSetup
  - void SetupPropertyBinder(VFXPropertyBinder propertyBinder, Transform transform)
  - void SetupRenderer(Renderer renderer, VisualEffect visualEffect)

### class RawMeshData
  (메서드 없음)

### struct Vertex
  (메서드 없음)

### struct Triangle
  (메서드 없음)

### class UniformMeshSamplingHelper
  - Mesh RendererToMesh(Renderer meshRenderer)
  - RawMeshData ComputeDataCache(Mesh input, bool useSubMesh, int submeshIndex)
  - RawMeshData.Vertex GetInterpolatedVertex(RawMeshData meshData, BarycentricTriangleSampling sampling)
  - BarycentricTriangleSampling GetNextSampling(RawMeshData meshData, System.Random rand)
  - (내부 메서드 2개 숨김)

### class UniformMeshSample : MonoBehaviour
  - bool _SaveAsNewPrefab()
  - void _ApplyPrefabChanges()
  - bool _LoadPrefab()
  - bool _InstantiateEffectPrefab(bool autoStartEffect)
  - void _BakeUniformMesh()
  - void _SetGraphicsBuffer()
  - void _FindRenderer()
  - void ConfigureVFXBinders()
  - void SetProperty_EffectActive(bool isActive)
  - void SendPlayEvent() → `vfxComponent?.Play` 위임 [wrapper]
  - void SendStopEvent() → `vfxComponent?.Stop` 위임 [wrapper]
  - void Start() → `SetupVfxGraph` 위임 [wrapper?]
  - void OnDisable() → `meshBaker.OnDisable` 위임 [wrapper?]
  - void SetNewEffectPrefab(GameObject newEffectPrefab)
  - void StartEffect()
  - void StopEffect()
  - void SetupVfxGraph()
  - (내부 메서드 3개 숨김)

### enum EffectState
  (메서드 없음)

## Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/Editor

### class UniformMeshSampleEditor : Editor
  - void OnEnable()
  - void OnInspectorGUI()
  - void Setup() → `EditorGUILayout.PropertyField` 위임 [wrapper?]
  - void EffectsLoading() → `EditorGUILayout.HelpBox` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

## Assets/INab Studio/Vfx Assets/Common/Utilities

### class VFXLossyTransformBinder : VFXBinderBase
  - void OnEnable() → `UpdateSubProperties` 위임 [wrapper?]
  - void OnValidate() → `UpdateSubProperties` 위임 [wrapper?]
  - bool IsValid(VisualEffect component) → `component.HasVector3` 위임 [wrapper?]
  - void UpdateBinding(VisualEffect component)
  - string ToString() → `string.Format` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

## Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts

### class TrailTransform : MonoBehaviour
  - (내부 메서드 1개 숨김)

### class TrailPresetSettings
  (메서드 없음)

### class WeaponTrailEffect : MonoBehaviour
  - TrailPresetSettings GetOrCreatePresetForClip(AnimationClip clip)
  - void EnsurePresetsForAllClips() → `GetOrCreatePresetForClip` 위임 [wrapper?]
  - void _RefreshAnimationClipList()
  - void EventSetTrailLength(TrailEventData data) → `data.target.SetProperty_Length` 위임 [wrapper?]
  - void EventStartTrail(TrailEventData data) → `data.target.StartTrail` 위임 [wrapper?]
  - void EventStopTrail(TrailEventData data) → `data.target.StopTrail` 위임 [wrapper?]
  - void AddTrailEventsAtStart()
  - void AutoPreviewStart()
  - void PlayTrailSegmentPreview(bool useStop)
  - void PlayFullClipPreview()
  - void EvaluatePreviewPose(Animator targetAnimator, AnimationClip clip, float time)
  - void _PreviewPoseAtTime(float time)
  - void DrawHandles()
  - bool _CheckSelectedPrefab()
  - bool _SaveAsNewPrefab()
  - void _ApplyPrefabChanges()
  - bool _LoadPrefab()
  - bool _InstantiateTrailPrefab()
  - void _CreateDefaultLineTransforms()
  - void ConfigureVFXBinders()
  - void SetProperty_EffectAlive(float value)
  - void SetProperty_EffectActive(bool isActive)
  - void SetProperty_Length(float value)
  - void SendPlayEvent()
  - void SendStopEvent()
  - void OnDisable() → `DisposePreviewGraph` 위임 [wrapper]
  - void OnDestroy() → `DisposePreviewGraph` 위임 [wrapper]
  - void SetLengthMultiplier(float newLengthMultiplier)
  - void SetNewTrailPrefab(GameObject newTrailPrefab)
  - void SetTrailLength(float trailLengthLifetime) → `SetProperty_Length` 위임 [wrapper?]
  - void StartTrailWithLength(float fadeInDuration, float trailLengthLifetime) → `StartTrail` 위임 [wrapper?]
  - void StartTrail(float fadeInDuration)
  - void StopTrail(float fadeOutDuration)
  - (내부 메서드 11개 숨김)

### enum EffectState
  (메서드 없음)

### enum TrailUsageType
  (메서드 없음)

### enum AnimationPlaybackMode
  (메서드 없음)

### class ClipPreset
  (메서드 없음)

### class TrailEventData : ScriptableObject
  (메서드 없음)

## Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/Editor

### class TrailTransformEditor : Editor
  - (내부 메서드 1개 숨김)

### class WeaponTrailEffectEditor : Editor
  - void OnInspectorGUI()
  - void OnSceneGUI() → `ourTarget.DrawHandles` 위임 [wrapper?]
  - void Setup() → `EditorGUILayout.PropertyField` 위임 [wrapper?]
  - void EffectsLoading() → `EditorGUILayout.HelpBox` 위임 [wrapper?]
  - (내부 메서드 5개 숨김)

## Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/API Examples

### class TrailAnimationEventsShowcase : MonoBehaviour
  - void CallStartTrail(float fadeInDuration)
  - void CallEndTrail(float fadeOutDuration)

### class TrailAPIShowcase : MonoBehaviour
  - void SetLengthPropertyWithSlider(float newValue) → `SetTrailLength` 위임 [wrapper?]
  - void SetTrailLength()
  - void StartTrail()
  - void EndTrail()
  - void ChangeLengthMultiplier()
  - void SetNewTrailPrefab(GameObject newPrefab)
  - void SetTrailPrefab1() → `StartTrail` 위임 [wrapper?]
  - void SetTrailPrefab2() → `StartTrail` 위임 [wrapper?]

## Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts

### class RotateAroundAxisTrail : MonoBehaviour
  - (내부 메서드 1개 숨김)

### class RuntimeAnimatorPlayer : MonoBehaviour
  - void Start() → `FindAnimations` 위임 [wrapper?]
  - void OnEnable() → `FindAnimations` 위임 [wrapper?]
  - void FindAnimations()
  - void PlaySelected()
  - void ChangedSlider(float value)
  - void ChangedAnimationSpeedSlider(float value)
  - (내부 메서드 2개 숨김)

### class ShowcaseAutoPlay : MonoBehaviour
  - void SetActiveCategory() → `trailCategories[selectedClipIndex].SetActive` 위임 [wrapper?]
  - void Start() → `SetActiveCategory` 위임 [wrapper?]
  - (내부 메서드 1개 숨김)

### class ShowcaseSpawnerTrail : MonoBehaviour
  - void OnEnable() → `PlayAll` 위임 [wrapper?]
  - void OnValidate() → `ChangleRotationSpeedAll` 위임 [wrapper?]
  - void SpawnPrefabs()
  - void AddTestPrefab()
  - void DestroyPrefabs() → `spawnedObjects.Clear` 위임 [wrapper?]
  - void PlayAll() → `obj.GetComponent<WeaponTrailEffect>().SetTrailLength` 위임 [wrapper?]
  - void StopAll() → `obj.GetComponent<WeaponTrailEffect>().StopTrail` 위임 [wrapper?]
  - void ChangleLengthAll() → `obj.GetComponent<WeaponTrailEffect>().SetTrailLength` 위임 [wrapper?]
  - void ChangleRotationSpeedAll() → `obj.GetComponent<RotateAroundAxisTrail>` 위임 [wrapper?]
  - void PauseAll() → `obj.GetComponent<RotateAroundAxisTrail>` 위임 [wrapper?]
  - void GetPrefabsFromChildren() → `obj.GetComponent<WeaponTrailEffect>` 위임 [wrapper?]

### class ShowcaseSpawnerTrailEditor : Editor
  - void OnInspectorGUI()

## Assets/Tests/EditMode/Occlusion

### class WallOcclusionRuntimeTests
  - void TearDown()
  - void BuildRange_PacksRadiiAndEnableFlag()
  - void BuildRange_ForcesOuterRadiusAboveInner()
  - void BuildRange_DisabledFlagIsZero()
  - void BuildRange_NullSettingsDisablesFade() → `Assert.That` 위임 [wrapper?]
  - void BuildShape_ClampsThresholdAndFalloffs()
  - void BuildShape_PacksFloorGuardDepth()
  - void Bind_SwapsMappedMaterialAndLeavesOthersAlone()
  - void Bind_IsIdempotent()
  - void Bind_ReportsUnmappedMaterialsByName()
  - void Bind_SwapsEveryMappedSlotOnMultiMaterialRenderer()
  - void Bind_DeduplicatesRepeatedRoots()
  - void Bind_WithoutMappingsDoesNothing()
  - void Bind_FindsRenderersOnInactiveChildren()
  - void Bind_SkipsRenderersExcludedByName()
  - void Bind_ExcludesRenderersUnderNamedModelRoot()
  - (내부 메서드 5개 숨김)

## Assets/Tests/EditMode/RuntimeSafety

### class RuntimeSafetyTests
  - void TearDown()
  - void Reconcile_LeavesExactlyOneEnabledServiceOfEachType()
  - void ScenePriority_PrefersGameplayThenLobbyThenLoading()
  - void UnreadableMeshColliderBakeScope_UsesTemporaryBoxAndRestoresSource()

## 위임(Facade) 관계 요약

- `IsCurrentAnimStateEqualTooStateNameCondition.OnStart()` → `UnityEngine.Animator.StringToHash` (Assets/1.Scripts/BT/Conditions/IsCurrentAnimStateEqualTooStateNameCondition.cs)
- `CameraTargetSwitcher.SwitchToNextTarget()` → `SwitchTarget` (Assets/1.Scripts/Camera/CameraTargetSwitcher.cs)
- `CameraTargetSwitcher.SwitchToPreviousTarget()` → `SwitchTarget` (Assets/1.Scripts/Camera/CameraTargetSwitcher.cs)
- `CameraTargetSwitcher.BindOwnerLifeCycleFromCurrentTarget()` → `BindOwnerLifeCycle` (Assets/1.Scripts/Camera/CameraTargetSwitcher.cs)
- `CameraTargetSwitcher.HandleOwnerLifeStateChanged()` → `SetSpectatorMode` (Assets/1.Scripts/Camera/CameraTargetSwitcher.cs)
- `CameraTargetSwitcher.RestoreFixedCameraRotation()` → `ApplyFixedCameraRotation` (Assets/1.Scripts/Camera/CameraTargetSwitcher.cs)
- `CameraTestPlayer.Awake()` → `GetComponent<Renderer>` (Assets/1.Scripts/Camera/CameraTestPlayer.cs)
- `CameraTestPlayer.OnNetworkSpawn()` → `ApplyClientColor` (Assets/1.Scripts/Camera/CameraTestPlayer.cs)
- `CameraFeedback.ReportLocalPlayerHit()` → `TryGenerateImpulse` (Assets/1.Scripts/Camera/Feedback/CameraFeedback.cs)
- `CameraFeedback.ReportLocalPlayerDealtDamage()` → `TryGenerateImpulse` (Assets/1.Scripts/Camera/Feedback/CameraFeedback.cs)
- `UnitCameraFeedbackReporter.Awake()` → `GetComponent<Unit>` (Assets/1.Scripts/Camera/Feedback/UnitCameraFeedbackReporter.cs)
- `FloatFollowTarget.SetSource()` → `RefreshPosition` (Assets/1.Scripts/Camera/FloatFollowTarget.cs)
- `FloatFollowTarget.SetFixedWorldY()` → `RefreshPosition` (Assets/1.Scripts/Camera/FloatFollowTarget.cs)
- `FloatFollowTarget.LateUpdate()` → `RefreshPosition` (Assets/1.Scripts/Camera/FloatFollowTarget.cs)
- `DevBuildSceneList.EnableDevScenes()` → `SetDevScenesEnabled` (Assets/1.Scripts/Dev/Editor/DevBuildSceneList.cs)
- `DevBuildSceneList.DisableDevScenes()` → `SetDevScenesEnabled` (Assets/1.Scripts/Dev/Editor/DevBuildSceneList.cs)
- `DevBuildSceneList.LogCurrentList()` → `Debug.Log` (Assets/1.Scripts/Dev/Editor/DevBuildSceneList.cs)
- `HitVFXDebugHUD.Describe()` → `type.Value.ToString` (Assets/1.Scripts/Dev/HitVFXDebugHUD.cs)
- `ProfilerWindow.OnDisable()` → `DisposeRecorders` (Assets/1.Scripts/Dev/Profiler/Editor/ProfilerWindow.cs)
- `ProfilerWindow.TryStartFirst()` → `TryStart` (Assets/1.Scripts/Dev/Profiler/Editor/ProfilerWindow.cs)
- `BuildWindowsPlayer.BuildWindows64FromMenu()` → `Build` (Assets/1.Scripts/Editor/BuildWindowsPlayer.cs)
- `BuildWindowsPlayer.ResolveOutputPath()` → `Path.GetFullPath` (Assets/1.Scripts/Editor/BuildWindowsPlayer.cs)
- `EffectSceneTesterEditor.Section()` → `EditorGUILayout.LabelField` (Assets/1.Scripts/Effects/Editor/EffectSceneTesterEditor.cs)
- `EffectEntry.OnValidate()` → `RecomputeLifetimes` (Assets/1.Scripts/Effects/EffectEntry.cs)
- `EffectHandle.Equals()` → `Equals` (Assets/1.Scripts/Effects/EffectHandle.cs)
- `EffectHitPoint.ResetWarnings()` → `Warned.Clear` (Assets/1.Scripts/Effects/EffectHitPoint.cs)
- `EffectManager.Play()` → `Play` (Assets/1.Scripts/Effects/EffectManager.cs)
- `EffectManager.PlayLooping()` → `PlayLoopingCore` (Assets/1.Scripts/Effects/EffectManager.cs)
- `EffectManager.PlayLooping()` → `PlayLoopingCore` (Assets/1.Scripts/Effects/EffectManager.cs)
- `EffectManager.DriverOf()` → `instance.GetComponent<EffectInstance>` (Assets/1.Scripts/Effects/EffectManager.cs)
- `EffectManager.PoolCountAll()` → `_pool.CountAll` (Assets/1.Scripts/Effects/EffectManager.cs)
- `EffectManager.PoolCountActive()` → `_pool.CountActive` (Assets/1.Scripts/Effects/EffectManager.cs)
- `EffectPool.Rent()` → `PoolFor(prefab).Get` (Assets/1.Scripts/Effects/EffectPool.cs)
- `EffectPool.CountAll()` → `_pools.TryGetValue` (Assets/1.Scripts/Effects/EffectPool.cs)
- `EffectPool.CountActive()` → `_pools.TryGetValue` (Assets/1.Scripts/Effects/EffectPool.cs)
- `EffectPool.Dispose()` → `_pools.Clear` (Assets/1.Scripts/Effects/EffectPool.cs)
- `EffectPrefabRules.ResetWarnings()` → `Warned.Clear` (Assets/1.Scripts/Effects/EffectPrefabRules.cs)
- `EffectSceneTester.Case6FreezeTarget()` → `SetTargetRate` (Assets/1.Scripts/Effects/EffectSceneTester.cs)
- `EffectSceneTester.Case6ResumeTarget()` → `SetTargetRate` (Assets/1.Scripts/Effects/EffectSceneTester.cs)
- `EffectTestMover.Update()` → `Mathf.Sin` (Assets/1.Scripts/Effects/EffectTestMover.cs)
- `HitVFXPlayback.ResetWarnings()` → `Warned.Clear` (Assets/1.Scripts/Effects/HitVFXPlayback.cs)
- `BombController.OnDrawGizmos()` → `Gizmos.DrawWireSphere` (Assets/1.Scripts/Enemy/Boss/BombController.cs)
- `BombController.Explode()` → `SetBombEnableClientRpc` (Assets/1.Scripts/Enemy/Boss/BombController.cs)
- `BombController.SetBombEnableClientRpc()` → `SetBombEnable` (Assets/1.Scripts/Enemy/Boss/BombController.cs)
- `BombController.SetFloorEnableClientRpc()` → `SetFloorEnable` (Assets/1.Scripts/Enemy/Boss/BombController.cs)
- `BombController.SetEnableClientRpc()` → `gameObject.SetActive` (Assets/1.Scripts/Enemy/Boss/BombController.cs)
- `ChargeController.SetDamage()` → `_floorColliderAttack.SetDamage` (Assets/1.Scripts/Enemy/Boss/ChargeController.cs)
- `ChargeController.SetFloorEnableClientRpc()` → `SetFloorActive` (Assets/1.Scripts/Enemy/Boss/ChargeController.cs)
- `ChargingObject.Awake()` → `CacheLocalPositions` (Assets/1.Scripts/Enemy/Boss/ChargingObject.cs)
- `ChargingObject.SetMinMaxY()` → `Mathf.Max` (Assets/1.Scripts/Enemy/Boss/ChargingObject.cs)
- `ChargingObject.BeginLowering()` → `SetColliderEnabled` (Assets/1.Scripts/Enemy/Boss/ChargingObject.cs)
- `ColliderBasicAttack.Awake()` → `GetComponent<KnockbackAttack>` (Assets/1.Scripts/Enemy/Boss/ColilderBasicAttack.cs)
- `ColliderBasicAttack.OnTriggerEnter()` → `OnAttackTriggerEnter` (Assets/1.Scripts/Enemy/Boss/ColilderBasicAttack.cs)
- `ColliderBasicAttack.OnTriggerStay()` → `OnAttackTriggerStay` (Assets/1.Scripts/Enemy/Boss/ColilderBasicAttack.cs)
- `ColliderBasicAttack.OnTriggerExit()` → `OnAttackTriggerExit` (Assets/1.Scripts/Enemy/Boss/ColilderBasicAttack.cs)
- `JumpController.SetDamage()` → `Mathf.Max` (Assets/1.Scripts/Enemy/Boss/JumpController.cs)
- `JumpController.EnableMeshRenderers()` → `mesh.SetActive` (Assets/1.Scripts/Enemy/Boss/JumpController.cs)
- `JumpController.ShowMyMeshClientRpc()` → `EnableMeshRenderers` (Assets/1.Scripts/Enemy/Boss/JumpController.cs)
- `JumpController.HideFloorsClientRpc()` → `SetFloorsEnable` (Assets/1.Scripts/Enemy/Boss/JumpController.cs)
- `KnockbackAttack.Awake()` → `InitializeAttackInfo` (Assets/1.Scripts/Enemy/Boss/KnockbackAttack.cs)
- `KnockbackAttack.SetKnockbackStrength()` → `Mathf.Max` (Assets/1.Scripts/Enemy/Boss/KnockbackAttack.cs)
- `TwentyThreeWells_Initializer.ApplyDamages()` → `Edit.LogError` (Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeWells_Initializer.cs)
- `TwentyThreeWells_Initializer.ApplyKnockbacks()` → `Edit.LogError` (Assets/1.Scripts/Enemy/Boss/Wells&No.23/TwentyThreeWells_Initializer.cs)
- `Enemy.PlayHitVFXRpc()` → `HitVFXPlayback.Play` (Assets/1.Scripts/Enemy/Enemy.cs)
- `MonsterTimeController.ResetTimeScale()` → `SetTimeScale` (Assets/1.Scripts/Enemy/MonsterTimeController.cs)
- `RunningOnlyOnServer.OnNetworkDespawn()` → `base.OnNetworkDespawn` (Assets/1.Scripts/Enemy/RunningOnlyOnServer.cs)
- `NetworkLoadingFlowController.Awake()` → `Debug.Log` (Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs)
- `NetworkLoadingFlowController.RegisterView()` → `ApplyViewState` (Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs)
- `NetworkLoadingFlowController.StartLocalProgressReporting()` → `StartCoroutine` (Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs)
- `NetworkLoadingFlowController.CalculateLocalLoadingProgress()` → `GetLocalSceneLoadProgress` (Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs)
- `NetworkLoadingFlowController.UnloadLoadingScene()` → `UnloadNetworkScene` (Assets/1.Scripts/Loading/NetworkLoadingFlowController.cs)
- `NetworkLoadingScreenView.OnEnable()` → `StartCoroutine` (Assets/1.Scripts/Loading/NetworkLoadingScreenView.cs)
- `NetworkLoadingScreenView.SetProgress()` → `ApplyProgress` (Assets/1.Scripts/Loading/NetworkLoadingScreenView.cs)
- `NetworkLoadingScreenView.SetPhase()` → `ApplyPhaseText` (Assets/1.Scripts/Loading/NetworkLoadingScreenView.cs)
- `NetworkLoadingScreenView.CompleteAndDestroy()` → `Destroy` (Assets/1.Scripts/Loading/NetworkLoadingScreenView.cs)
- `LobbyPlayerSlotView.SetState()` → `SetReady` (Assets/1.Scripts/Lobby/LobbyPlayerSlotView.cs)
- `LobbyUIController.ToggleLocalReady()` → `SetLocalReady` (Assets/1.Scripts/Lobby/LobbyUIController.cs)
- `GameManagerMainGameReadyTests.SetUp()` → `_gameObject.AddComponent<GameManager>` (Assets/1.Scripts/Managers/Editor/GameManagerMainGameReadyTests.cs)
- `GameManagerMainGameReadyTests.TearDown()` → `Object.DestroyImmediate` (Assets/1.Scripts/Managers/Editor/GameManagerMainGameReadyTests.cs)
- `GameManagerMainGameReadyTests.SetState()` → `SetStateMethod.Invoke` (Assets/1.Scripts/Managers/Editor/GameManagerMainGameReadyTests.cs)
- `GameManager.UnsubscribeMainGameStart()` → `Instance?.UnsubscribeMainGameReady` (Assets/1.Scripts/Managers/GameManager.cs)
- `LobbySceneManager.ApplyConnectionData()` → `TryApplyConnectionData` (Assets/1.Scripts/Managers/LobbySceneManager.cs)
- `LobbySceneManager.HandleLobbyStateChanged()` → `ApplyRoleUi` (Assets/1.Scripts/Managers/LobbySceneManager.cs)
- `MapSceneManager.CancelClientExit()` → `SetWarningPanel` (Assets/1.Scripts/Managers/MapSceneManager.cs)
- `MapSceneManager.OpenOptionPanel()` → `SetOptionPanel` (Assets/1.Scripts/Managers/MapSceneManager.cs)
- `MapSceneManager.CloseOptionPanel()` → `SetOptionPanel` (Assets/1.Scripts/Managers/MapSceneManager.cs)
- `MapSceneManager.ToggleOptionPanel()` → `SetOptionPanel` (Assets/1.Scripts/Managers/MapSceneManager.cs)
- `NemoSceneManager.Awake()` → `ResolveCommonReferences` (Assets/1.Scripts/Managers/NemoSceneManager.cs)
- `NemoSceneManager.FadeIn()` → `StartFade` (Assets/1.Scripts/Managers/NemoSceneManager.cs)
- `NemoSceneManager.FadeOut()` → `StartFade` (Assets/1.Scripts/Managers/NemoSceneManager.cs)
- `NemoSceneManager.FindButton()` → `target.GetComponent<Button>` (Assets/1.Scripts/Managers/NemoSceneManager.cs)
- `NemoSceneManager.WarnMissingReference()` → `Debug.LogWarning` (Assets/1.Scripts/Managers/NemoSceneManager.cs)
- `NemoSceneManager.BeginTransition()` → `Debug.Log` (Assets/1.Scripts/Managers/NemoSceneManager.cs)
- `NemoSceneManager.EndTransition()` → `Debug.Log` (Assets/1.Scripts/Managers/NemoSceneManager.cs)
- `NemoSceneManager.StartFade()` → `StartCoroutine` (Assets/1.Scripts/Managers/NemoSceneManager.cs)
- `SessionResult.FormatSurvival()` → `Mathf.FloorToInt` (Assets/1.Scripts/Managers/SessionResult.cs)
- `SessionStatsTracker.HasAnyPlayer()` → `FindAnyObjectByType<PlayerLifeCycleController>` (Assets/1.Scripts/Managers/SessionStatsTracker.cs)
- `TitleOptionsPanel.OnEnable()` → `ShowGameplay` (Assets/1.Scripts/Managers/TitleOptionsPanel.cs)
- `TitleOptionsPanel.ShowGameplay()` → `ShowOnly` (Assets/1.Scripts/Managers/TitleOptionsPanel.cs)
- `TitleOptionsPanel.ShowGraphics()` → `ShowOnly` (Assets/1.Scripts/Managers/TitleOptionsPanel.cs)
- `TitleOptionsPanel.ShowControls()` → `ShowOnly` (Assets/1.Scripts/Managers/TitleOptionsPanel.cs)
- `TitleOptionsPanel.ShowAudio()` → `ShowOnly` (Assets/1.Scripts/Managers/TitleOptionsPanel.cs)
- `TitleSceneManager.ToggleOption()` → `SetOptionPanel` (Assets/1.Scripts/Managers/TitleSceneManager.cs)
- `TitleSceneManager.OpenOption()` → `SetOptionPanel` (Assets/1.Scripts/Managers/TitleSceneManager.cs)
- `TitleSceneManager.CloseOption()` → `SetOptionPanel` (Assets/1.Scripts/Managers/TitleSceneManager.cs)
- `TitleSceneManager.SetTitleButtonsInteractable()` → `SetButtonsInteractable` (Assets/1.Scripts/Managers/TitleSceneManager.cs)
- `BossArenaContext.Awake()` → `Resolve` (Assets/1.Scripts/Map/BossArenaContext.cs)
- `BossArenaContext.FindChildByName()` → `GetComponentsInChildren<Transform>` (Assets/1.Scripts/Map/BossArenaContext.cs)
- `BossArenaContext.FindChildColliderByTag()` → `GetComponentsInChildren<Collider>` (Assets/1.Scripts/Map/BossArenaContext.cs)
- `BossEncounterDirector.FindLandingPointByName()` → `GameObject.Find` (Assets/1.Scripts/Map/BossEncounterDirector.cs)
- `BossEncounterDirector.BeginDescent()` → `SetPhase` (Assets/1.Scripts/Map/BossEncounterDirector.cs)
- `BossEnterTrigger.Awake()` → `GetComponent<BoxCollider>` (Assets/1.Scripts/Map/BossEnterTrigger.cs)
- `BossEnterTrigger.OnTriggerEnter()` → `Track` (Assets/1.Scripts/Map/BossEnterTrigger.cs)
- `BossEnterTrigger.OnTriggerExit()` → `Track` (Assets/1.Scripts/Map/BossEnterTrigger.cs)
- `ConveyorGroup.OnValidate()` → `Mathf.Max` (Assets/1.Scripts/Map/ConveyorGroup.cs)
- `ConveyorTile.Awake()` → `ResolveGroup` (Assets/1.Scripts/Map/ConveyorTile.cs)
- `ConveyorTile.ResolveGroup()` → `GetComponentInParent<ConveyorGroup>` (Assets/1.Scripts/Map/ConveyorTile.cs)
- `BossArenaWiring.FindChild()` → `root.GetComponentsInChildren<Transform>(true)
               .FirstOrDefault` (Assets/1.Scripts/Map/Editor/BossArenaWiring.cs)
- `BossEncounterWiring.FindLandingPoint()` → `Object.FindObjectsByType<Transform>` (Assets/1.Scripts/Map/Editor/BossEncounterWiring.cs)
- `GroundLayerAuthoring.DryRun()` → `Execute` (Assets/1.Scripts/Map/Editor/GroundLayerAuthoring.cs)
- `GroundLayerAuthoring.Apply()` → `Execute` (Assets/1.Scripts/Map/Editor/GroundLayerAuthoring.cs)
- `GroundLayerAuthoring.Normalize()` → `name.Substring` (Assets/1.Scripts/Map/Editor/GroundLayerAuthoring.cs)
- `GroundLayerAuthoring.Bump()` → `map.TryGetValue` (Assets/1.Scripts/Map/Editor/GroundLayerAuthoring.cs)
- `MapColliderAuthoring.ContainsAny()` → `lowerName.Contains` (Assets/1.Scripts/Map/Editor/MapColliderAuthoring.cs)
- `SlotAuthoringModel.IsAuthored()` → `slot.TryGetYaw` (Assets/1.Scripts/Map/Editor/SlotAuthoringModel.cs)
- `SlotAuthoringModel.CountDeadEntries()` → `slot.Rotations.Count` (Assets/1.Scripts/Map/Editor/SlotAuthoringModel.cs)
- `ZoneBridgeGateWiring.Collect()` → `root.GetComponentsInChildren<Transform>(true)
               .Where(t => t != root.transform && t.name.StartsWith(prefix))
               .OrderBy(t => t.name)
               .ToList` (Assets/1.Scripts/Map/Editor/ZoneBridgeGateWiring.cs)
- `ZoneBridgeGateWiring.Names()` → `string.Join` (Assets/1.Scripts/Map/Editor/ZoneBridgeGateWiring.cs)
- `ZoneRotationAuthoringWindow.Open()` → `GetWindow<ZoneRotationAuthoringWindow>` (Assets/1.Scripts/Map/Editor/ZoneRotationAuthoringWindow.cs)
- `ZoneRotationAuthoringWindow.OnEnable()` → `Refresh` (Assets/1.Scripts/Map/Editor/ZoneRotationAuthoringWindow.cs)
- `ZoneRotationAuthoringWindow.OnDisable()` → `ClearSpawn` (Assets/1.Scripts/Map/Editor/ZoneRotationAuthoringWindow.cs)
- `ZoneWiring.GenRandom()` → `RunGen` (Assets/1.Scripts/Map/Editor/ZoneWiring.cs)
- `ZoneWiring.Gen12345()` → `RunGen` (Assets/1.Scripts/Map/Editor/ZoneWiring.cs)
- `MapGenerator.EditorTestGenerate()` → `Generate` (Assets/1.Scripts/Map/MapGenerator.cs)
- `MapNavMeshBaker.HandleGenerated()` → `Bake` (Assets/1.Scripts/Map/MapNavMeshBaker.cs)
- `MapNavMeshBaker.RebakeNow()` → `Bake` (Assets/1.Scripts/Map/MapNavMeshBaker.cs)
- `MapNavMeshBaker.ReattachAgents()` → `Object.FindObjectsByType<NavMeshAgent>` (Assets/1.Scripts/Map/MapNavMeshBaker.cs)
- `MapNetworkSync.ComposeDifficultyLevel()` → `Mathf.Max` (Assets/1.Scripts/Map/MapNetworkSync.cs)
- `MapOverviewUI.Hide()` → `DestroyCanvas` (Assets/1.Scripts/Map/MapOverviewUI.cs)
- `MapOverviewUI.OnDestroy()` → `DestroyCanvas` (Assets/1.Scripts/Map/MapOverviewUI.cs)
- `MinimapMarker.OnEnable()` → `_all.Add` (Assets/1.Scripts/Map/Minimap/MinimapMarker.cs)
- `MinimapMarker.OnDisable()` → `_all.Remove` (Assets/1.Scripts/Map/Minimap/MinimapMarker.cs)
- `MinimapNetworkSync.Awake()` → `GetComponent<MinimapController>` (Assets/1.Scripts/Map/Minimap/MinimapNetworkSync.cs)
- `MovingPlatform.IsViaNode()` → `wp.GetComponent<WaypointNode>` (Assets/1.Scripts/Map/MovingPlatform.cs)
- `Vent.Awake()` → `SetDamageColliderActive` (Assets/1.Scripts/Map/Vent.cs)
- `BakeOpenScope.Dispose()` → `gate.ApplyOpenProgress` (Assets/1.Scripts/Map/ZoneBridgeGate.cs)
- `ZoneBridgeGateManager.OnNetworkDespawn()` → `base.OnNetworkDespawn` (Assets/1.Scripts/Map/ZoneBridgeGateManager.cs)
- `ZoneBridgeGateManager.HandleGatesChanged()` → `ApplyAllStates` (Assets/1.Scripts/Map/ZoneBridgeGateManager.cs)
- `ZoneBridgeGateManager.ApplyAllStates()` → `ApplyState` (Assets/1.Scripts/Map/ZoneBridgeGateManager.cs)
- `GateState.Equals()` → `OpenStartServerTime.Equals` (Assets/1.Scripts/Map/ZoneBridgeGateManager.cs)
- `ZoneLayout.OnDrawGizmosSelected()` → `ResolveSpawnEntries` (Assets/1.Scripts/Map/ZoneLayout.cs)
- `BossBase.OnNetworkDespawn()` → `base.OnNetworkDespawn` (Assets/1.Scripts/Monster/Boss/BossBase.cs)
- `BossBase.DespawnAfter()` → `DespawnNow` (Assets/1.Scripts/Monster/Boss/BossBase.cs)
- `BossBase.CooldownReady()` → `Mathf.Max` (Assets/1.Scripts/Monster/Boss/BossBase.cs)
- `BossBase.IsTargetValid()` → `MonsterTargeting.IsAttackable` (Assets/1.Scripts/Monster/Boss/BossBase.cs)
- `BossBase.OnStateChanged()` → `PlayStateAnimation` (Assets/1.Scripts/Monster/Boss/BossBase.cs)
- `BossBasicAttackChoice.Awake()` → `attackChoices.Add` (Assets/1.Scripts/Monster/Boss/BossBasicAttackChoice.cs)
- `GauntletBot.PlayAttackAnimClientRpc()` → `SafeCrossFade` (Assets/1.Scripts/Monster/Boss/GauntletBot.cs)
- `SpinnerBot.PlaySpinStartClientRpc()` → `SafeCrossFade` (Assets/1.Scripts/Monster/Boss/SpinnerBot.cs)
- `SpinnerBot.PlaySpinLoopClientRpc()` → `SafeCrossFade` (Assets/1.Scripts/Monster/Boss/SpinnerBot.cs)
- `SpinnerBot.PlayWhipClientRpc()` → `SafeCrossFade` (Assets/1.Scripts/Monster/Boss/SpinnerBot.cs)
- `SpinnerBot.PlayDizzyClientRpc()` → `SafeSetBool` (Assets/1.Scripts/Monster/Boss/SpinnerBot.cs)
- `DissolveDeath.DelayThenComplete()` → `onComplete?.Invoke` (Assets/1.Scripts/Monster/DissolveDeath.cs)
- `DissolveDeath.ApplyValue()` → `r.sharedMaterial.HasProperty` (Assets/1.Scripts/Monster/DissolveDeath.cs)
- `MonsterAnimationEventRelay.Awake()` → `GetComponentInParent<MonsterBase>` (Assets/1.Scripts/Monster/MonsterAnimationEventRelay.cs)
- `MonsterBase.OnNetworkDespawn()` → `base.OnNetworkDespawn` (Assets/1.Scripts/Monster/MonsterBase.cs)
- `MonsterBase.IsTargetValid()` → `MonsterTargeting.IsAttackable` (Assets/1.Scripts/Monster/MonsterBase.cs)
- `MonsterBase.PlayHitVFXRpc()` → `HitVFXPlayback.Play` (Assets/1.Scripts/Monster/MonsterBase.cs)
- `MonsterBase.DespawnAfter()` → `DespawnNow` (Assets/1.Scripts/Monster/MonsterBase.cs)
- `MonsterBase.CooldownReady()` → `Mathf.Max` (Assets/1.Scripts/Monster/MonsterBase.cs)
- `MonsterDeathEvents.RaiseServerMonsterDied()` → `ServerMonsterDied?.Invoke` (Assets/1.Scripts/Monster/MonsterDeathEvents.cs)
- `MonsterMeleeAttack.BeginHitWindow()` → `_windowHits.Clear` (Assets/1.Scripts/Monster/MonsterMeleeAttack.cs)
- `MonsterMeleeAttack.EndHitWindow()` → `_windowHits.Clear` (Assets/1.Scripts/Monster/MonsterMeleeAttack.cs)
- `MonsterMeleeAttack.Awake()` → `Mathf.Max` (Assets/1.Scripts/Monster/MonsterMeleeAttack.cs)
- `MonsterRangedAttack.Awake()` → `GetComponentInParent<Unit>` (Assets/1.Scripts/Monster/MonsterRangedAttack.cs)
- `MonsterSpawner.CountAlive()` → `_alive.RemoveAll` (Assets/1.Scripts/Monster/MonsterSpawner.cs)
- `MonsterTargeting.IsAttackable()` → `IsAttackable` (Assets/1.Scripts/Monster/MonsterTargeting.cs)
- `ForProfile.Start()` → `SubscribeToServerStarted` (Assets/1.Scripts/Network/ForProfile.cs)
- `ForProfile.OnDisable()` → `UnsubscribeFromServerStarted` (Assets/1.Scripts/Network/ForProfile.cs)
- `NetworkSessionLauncher.StartHost()` → `StartHostCore` (Assets/1.Scripts/Network/NetworkSessionLauncher.cs)
- `NetworkSessionLauncher.StartClient()` → `StartClientCore` (Assets/1.Scripts/Network/NetworkSessionLauncher.cs)
- `NetworkSessionLauncher.StartServer()` → `StartServerCore` (Assets/1.Scripts/Network/NetworkSessionLauncher.cs)
- `NetworkSessionLauncher.OnSetConnectionData()` → `SetDirectConnectionData` (Assets/1.Scripts/Network/NetworkSessionLauncher.cs)
- `NetworkSessionLauncher.OnSetConnectionData()` → `SetDirectConnectionData` (Assets/1.Scripts/Network/NetworkSessionLauncher.cs)
- `NetworkSessionLauncher.BeginHost()` → `CompleteSessionStartAsync` (Assets/1.Scripts/Network/NetworkSessionLauncher.cs)
- `NetworkSessionLauncher.BeginClient()` → `CompleteSessionStartAsync` (Assets/1.Scripts/Network/NetworkSessionLauncher.cs)
- `DirectIPv4ConnectionProvider.SetConnectionData()` → `ApplyConnectionData` (Assets/1.Scripts/Network/Session/DirectIPv4ConnectionProvider.cs)
- `UnityServicesBootstrap.BeginInitialization()` → `InitializeAsync` (Assets/1.Scripts/Network/UnityServicesBootstrap.cs)
- `PlayerCorpseController.HandleCorpseVisibilityChanged()` → `ConfigurePhysics` (Assets/1.Scripts/Player/Corpse/PlayerCorpseController.cs)
- `DashLog.Log()` → `Debug.Log` (Assets/1.Scripts/Player/Dash/DashLog.cs)
- `DashLog.LogWarning()` → `Debug.LogWarning` (Assets/1.Scripts/Player/Dash/DashLog.cs)
- `DashRuntimeConfig.IsPositiveFinite()` → `double.IsNaN` (Assets/1.Scripts/Player/Dash/DashRuntimeConfig.cs)
- `DashRuntimeConfig.IsNonNegativeFinite()` → `double.IsNaN` (Assets/1.Scripts/Player/Dash/DashRuntimeConfig.cs)
- `DashValidationPolicy.IsFinite()` → `double.IsNaN` (Assets/1.Scripts/Player/Dash/DashValidationPolicy.cs)
- `PlayerDashData.CreateValidatedConfig()` → `DashRuntimeConfig.Create` (Assets/1.Scripts/Player/Dash/PlayerDashData.cs)
- `DashRuntimeConfigTests.ZeroDashDuration_Disabled()` → `Assert.IsFalse` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashRuntimeConfigTests.cs)
- `DashRuntimeConfigTests.NegativeRecharge_Disabled()` → `Assert.IsFalse` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashRuntimeConfigTests.cs)
- `DashRuntimeConfigTests.NaNDashSpeed_Disabled()` → `Assert.IsFalse` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashRuntimeConfigTests.cs)
- `DashSnapshotHistoryTests.Empty_SelectFails()` → `Assert.IsFalse` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashSnapshotHistoryTests.cs)
- `DashValidationPolicyTests.Validate()` → `DashValidationPolicy.Validate` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DashValidationPolicyTests.ConfigDisabled_Rejected()` → `Assert.AreEqual` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DashValidationPolicyTests.NaNDirection_RejectedAsInvalidPayload()` → `Assert.AreEqual` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DashValidationPolicyTests.ZeroDirection_RejectedAsInvalidPayload()` → `Assert.AreEqual` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DashValidationPolicyTests.RttUnavailable_Rejected()` → `Assert.AreEqual` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DashValidationPolicyTests.NoSnapshot_Rejected()` → `Assert.AreEqual` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DashValidationPolicyTests.NotGroundedAtSnapshot_Rejected()` → `Assert.AreEqual` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DashValidationPolicyTests.NoAuthoritativeCharge_Rejected()` → `Assert.AreEqual` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DashValidationPolicyTests.DeadBeforeGrounded_DeadReasonWins_ValidationOrder()` → `Assert.AreEqual` (Assets/1.Scripts/Player/Dash/Tests/EditMode/DashValidationPolicyTests.cs)
- `DefaultAttackController.GetAttackStateHash()` → `Animator.StringToHash` (Assets/1.Scripts/Player/DefaultAttackController.cs)
- `DefaultAttackController.HasComboWindowOpenEvent()` → `nameof` (Assets/1.Scripts/Player/DefaultAttackController.cs)
- `PlayerFallController.Awake()` → `GetComponent<PlayerEncounterLock>` (Assets/1.Scripts/Player/Fall/PlayerFallController.cs)
- `PlayerFallRecovery.Awake()` → `ResolveReferences` (Assets/1.Scripts/Player/Fall/PlayerFallRecovery.cs)
- `PlayerFallRecovery.HandleServerFallDeath()` → `ReturnAfterFallDeathRpc` (Assets/1.Scripts/Player/Fall/PlayerFallRecovery.cs)
- `PlayerFallRecovery.BeginRecoveryRpc()` → `StartCoroutine` (Assets/1.Scripts/Player/Fall/PlayerFallRecovery.cs)
- `PlayerLifeCycleController.TryBeginDeathPresentation()` → `TryBeginDeathPresentation` (Assets/1.Scripts/Player/Life/PlayerLifeCycleController.cs)
- `PlayerLifeCycleController.TryEnterSoul()` → `TryTransition` (Assets/1.Scripts/Player/Life/PlayerLifeCycleController.cs)
- `PlayerLifeCycleController.TryEnterPermanentDead()` → `TryTransition` (Assets/1.Scripts/Player/Life/PlayerLifeCycleController.cs)
- `PlayerLifeCycleController.TryEnterResolvedDeathState()` → `TryEnterPermanentDead` (Assets/1.Scripts/Player/Life/PlayerLifeCycleController.cs)
- `PlayerLifeInputPolicy.Awake()` → `ResolveReferences` (Assets/1.Scripts/Player/Life/PlayerLifeInputPolicy.cs)
- `PlayerLifeInputPolicy.Start()` → `ApplyCurrentAccess` (Assets/1.Scripts/Player/Life/PlayerLifeInputPolicy.cs)
- `PlayerLifeInputPolicy.HandleGameplayAccessChanged()` → `ApplyAccess` (Assets/1.Scripts/Player/Life/PlayerLifeInputPolicy.cs)
- `PlayerLifeInputPolicy.HandleCinematicLockChanged()` → `ApplyCurrentAccess` (Assets/1.Scripts/Player/Life/PlayerLifeInputPolicy.cs)
- `PlayerReviveController.Awake()` → `ResolveGameRuleReference` (Assets/1.Scripts/Player/Life/PlayerReviveController.cs)
- `PlayerReviveController.RequestDebugReviveRpc()` → `TryCompleteReviveOnServer` (Assets/1.Scripts/Player/Life/PlayerReviveController.cs)
- `PlayerLifeCountEntry.NetworkSerialize()` → `serializer.SerializeValue` (Assets/1.Scripts/Player/Life/Temp_MultiGameRule.cs)
- `PlayerLifeCountEntry.Equals()` → `Equals` (Assets/1.Scripts/Player/Life/Temp_MultiGameRule.cs)
- `Temp_MultiGameRule.HasReviveAvailable()` → `TryGetLifeCount` (Assets/1.Scripts/Player/Life/Temp_MultiGameRule.cs)
- `Temp_MultiGameRule.HandleClientConnected()` → `TryRegisterClient` (Assets/1.Scripts/Player/Life/Temp_MultiGameRule.cs)
- `Temp_MultiGameRule.HandleClientDisconnected()` → `TryUnregisterClient` (Assets/1.Scripts/Player/Life/Temp_MultiGameRule.cs)
- `Temp_MultiGameRule.HandleLifeCountsChanged()` → `LifeCountsChanged?.Invoke` (Assets/1.Scripts/Player/Life/Temp_MultiGameRule.cs)
- `PlayableCharacterVisual.BindExistingVisual()` → `BindVisual` (Assets/1.Scripts/Player/PlayableCharacterVisual.cs)
- `Player.OnGainedOwnership()` → `ConfigureMovementPhysicsAuthority` (Assets/1.Scripts/Player/Player.cs)
- `Player.OnLostOwnership()` → `ConfigureMovementPhysicsAuthority` (Assets/1.Scripts/Player/Player.cs)
- `Player.EndDefaultAttack()` → `defaultAttack.EndCurrentAttack` (Assets/1.Scripts/Player/Player.cs)
- `Player.HitDefaultAttack()` → `defaultAttack.HitCurrentAttack` (Assets/1.Scripts/Player/Player.cs)
- `Player.HandleDefaultAttackEvent()` → `defaultAttack.HandleAnimationEvent` (Assets/1.Scripts/Player/Player.cs)
- `Player.EndInterrupt()` → `stateController.EndInterrupt` (Assets/1.Scripts/Player/Player.cs)
- `Player.BeginAttackState()` → `stateController.ChangeState` (Assets/1.Scripts/Player/Player.cs)
- `Player.BeginGrabbedByInstigator()` → `BeginRestrainedByInstigator` (Assets/1.Scripts/Player/Player.cs)
- `Player.EndGrabbedByInstigator()` → `EndRestrainedByInstigator` (Assets/1.Scripts/Player/Player.cs)
- `Player.NotifyKnockbackEndedServerRpc()` → `stateController.EndKnockback` (Assets/1.Scripts/Player/Player.cs)
- `Player.TakeDamage()` → `base.TakeDamage` (Assets/1.Scripts/Player/Player.cs)
- `PlayerAnimationEventRelay.EndDefaultAttack()` → `HandleDefaultAttackEvent` (Assets/1.Scripts/Player/PlayerAnimationEventRelay.cs)
- `PlayerAnimationEventRelay.HitDefaultAttack()` → `HandleDefaultAttackEvent` (Assets/1.Scripts/Player/PlayerAnimationEventRelay.cs)
- `PlayerDefaultAttack.Awake()` → `SetAttackType` (Assets/1.Scripts/Player/PlayerDefaultAttack.cs)
- `PlayerEncounterLock.Awake()` → `ResolveReferences` (Assets/1.Scripts/Player/PlayerEncounterLock.cs)
- `PlayerEncounterLock.HandleLockChanged()` → `ApplyLocalLock` (Assets/1.Scripts/Player/PlayerEncounterLock.cs)
- `PlayerGroundingSensor.IsOwnCollider()` → `candidate.transform.IsChildOf` (Assets/1.Scripts/Player/PlayerGroundingSensor.cs)
- `PlayerGroundingSensor.OnValidate()` → `Mathf.Max` (Assets/1.Scripts/Player/PlayerGroundingSensor.cs)
- `PlayerInputReader.Start()` → `RefreshControlState` (Assets/1.Scripts/Player/PlayerInputReader.cs)
- `PlayerInputReader.OnNetworkSpawn()` → `RefreshControlState` (Assets/1.Scripts/Player/PlayerInputReader.cs)
- `PlayerInputReader.OnNetworkDespawn()` → `base.OnNetworkDespawn` (Assets/1.Scripts/Player/PlayerInputReader.cs)
- `PlayerInputReader.SetInputEnabled()` → `ApplyInputState` (Assets/1.Scripts/Player/PlayerInputReader.cs)
- `PlayerInputReader.SetUiInputSuppressed()` → `ApplyInputState` (Assets/1.Scripts/Player/PlayerInputReader.cs)
- `PlayerInputReader.RefreshControlState()` → `SetLocalControl` (Assets/1.Scripts/Player/PlayerInputReader.cs)
- `PlayerInvulnerability.OnNetworkDespawn()` → `base.OnNetworkDespawn` (Assets/1.Scripts/Player/PlayerInvulnerability.cs)
- `PlayerInvulnerability.HandleServerInvulnerableChanged()` → `ApplyHurtboxState` (Assets/1.Scripts/Player/PlayerInvulnerability.cs)
- `PlayerLandingProtection.Awake()` → `ResolveReferences` (Assets/1.Scripts/Player/PlayerLandingProtection.cs)
- `PlayerMovement.Update()` → `Rotate` (Assets/1.Scripts/Player/PlayerMovement.cs)
- `PlayerMovement.MoveRoot()` → `rb.MovePosition` (Assets/1.Scripts/Player/PlayerMovement.cs)
- `PlayerRootMotionRelay.Awake()` → `GetComponentInParent<DefaultAttackController>` (Assets/1.Scripts/Player/PlayerRootMotionRelay.cs)
- `PlayerStateController.DescribeCaller()` → `string.IsNullOrEmpty` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerStateController.ApplyRestrainedFromServer()` → `ApplyRestrained` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerStateController.BeginRestrained()` → `TryReceiveRestraint` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerStateController.ApplyKnockbackFromServer()` → `BeginKnockback` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerIdleState.Enter()` → `Context.Player.SetAnimatorMoving` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerMoveState.Enter()` → `Context.Player.SetAnimatorMoving` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerAttackState.Enter()` → `Context.DefaultAttack.BeginFromState` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerAttackState.Tick()` → `Context.DefaultAttack.Tick` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerLockedState.Enter()` → `Context.Player.SetAnimatorMoving` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerKnockbackState.EndAndNotifyServer()` → `Context.Player.NotifyKnockbackEnded` (Assets/1.Scripts/Player/PlayerStateController.cs)
- `PlayerUiInputPolicy.Awake()` → `ResolveReferences` (Assets/1.Scripts/Player/PlayerUiInputPolicy.cs)
- `PlayerUiInputPolicy.Start()` → `ApplyCurrentState` (Assets/1.Scripts/Player/PlayerUiInputPolicy.cs)
- `PlayerUiInputPolicy.HandleBlockedChanged()` → `ApplyBlockedState` (Assets/1.Scripts/Player/PlayerUiInputPolicy.cs)
- `PlayerUiInputPolicy.ApplyCurrentState()` → `ApplyBlockedState` (Assets/1.Scripts/Player/PlayerUiInputPolicy.cs)
- `FirstMeleeMainSkill.OnAimUpdated()` → `Flatten` (Assets/1.Scripts/Player/Skill/FirstMeleeMainSkill.cs)
- `FirstMeleePassive.Awake()` → `GetComponent<PlayerDefaultAttack>` (Assets/1.Scripts/Player/Skill/FirstMeleePassive.cs)
- `FirstMeleeUltimateSkill.OnServerStart()` → `base.OnServerStart` (Assets/1.Scripts/Player/Skill/FirstMeleeUltimateSkill.cs)
- `FirstMeleeUltimateSkill.OnEnd()` → `base.OnEnd` (Assets/1.Scripts/Player/Skill/FirstMeleeUltimateSkill.cs)
- `PlayerSkillBase.SetDamageSnapshot()` → `Mathf.Max` (Assets/1.Scripts/Player/Skill/PlayerSkillBase.cs)
- `PlayerSkillController.GetCooldownRemaining()` → `Mathf.Max` (Assets/1.Scripts/Player/Skill/PlayerSkillController.cs)
- `PlayerSkillController.TryUse()` → `Cast` (Assets/1.Scripts/Player/Skill/PlayerSkillController.cs)
- `PlayerSkillController.ExecuteTargetedSkill()` → `Cast` (Assets/1.Scripts/Player/Skill/PlayerSkillController.cs)
- `PlayerSkillController.WasSkillRePressed()` → `inputReader.GetSkillPressed` (Assets/1.Scripts/Player/Skill/PlayerSkillController.cs)
- `PlayerSkillController.ResolveTarget()` → `targetRef.TryGet` (Assets/1.Scripts/Player/Skill/PlayerSkillController.cs)
- `PlayerSkillState.Tick()` → `Context.Skills?.Tick` (Assets/1.Scripts/Player/Skill/PlayerSkillState.cs)
- `PlayerSkillState.Exit()` → `Context.Skills?.HandleSkillStateExit` (Assets/1.Scripts/Player/Skill/PlayerSkillState.cs)
- `PlayerSkillTargeting.Cancel()` → `StopMoveToCast` (Assets/1.Scripts/Player/Skill/Targeting/PlayerSkillTargeting.cs)
- `SkillCursorView.Resolve()` → `FirstAssigned` (Assets/1.Scripts/Player/Skill/Targeting/SkillCursorView.cs)
- `SkillRangeIndicator.Awake()` → `HideAll` (Assets/1.Scripts/Player/Skill/Targeting/SkillRangeIndicator.cs)
- `PlayerSoulController.HandleLifeStateChanged()` → `ApplyLifeState` (Assets/1.Scripts/Player/Soul/PlayerSoulController.cs)
- `PlayerSoulController.HandleCharacterApplied()` → `SetCharacterDefinition` (Assets/1.Scripts/Player/Soul/PlayerSoulController.cs)
- `PlayerSoulController.OnValidate()` → `Mathf.Max` (Assets/1.Scripts/Player/Soul/PlayerSoulController.cs)
- `Callbacks.RunStarted()` → `Debug.Log` (Assets/1.Scripts/Rendering/Editor/WallOcclusionTestRunner.cs)
- `FogPainterWindow.Open()` → `GetWindow<FogPainterWindow>` (Assets/1.Scripts/Rendering/Fog/Editor/FogPainterWindow.cs)
- `FogPainterWindow.OnEnable()` → `TryAutoFind` (Assets/1.Scripts/Rendering/Fog/Editor/FogPainterWindow.cs)
- `FogPainterWindow.OnDisable()` → `FlushIfDirty` (Assets/1.Scripts/Rendering/Fog/Editor/FogPainterWindow.cs)
- `FogManager.Unregister()` → `s_volumes.Remove` (Assets/1.Scripts/Rendering/Fog/FogManager.cs)
- `FogManager.LateUpdate()` → `PushGlobals` (Assets/1.Scripts/Rendering/Fog/FogManager.cs)
- `FogRendererFeature.Dispose()` → `CoreUtils.Destroy` (Assets/1.Scripts/Rendering/Fog/FogRendererFeature.cs)
- `FogVolume.OnEnable()` → `FogManager.Register` (Assets/1.Scripts/Rendering/Fog/FogVolume.cs)
- `FogVolume.OnDisable()` → `FogManager.Unregister` (Assets/1.Scripts/Rendering/Fog/FogVolume.cs)
- `FogVolume.GetWorldToLocal()` → `Matrix4x4.TRS` (Assets/1.Scripts/Rendering/Fog/FogVolume.cs)
- `WallOcclusionGlobals.Disable()` → `Shader.SetGlobalVector` (Assets/1.Scripts/Rendering/Occlusion/WallOcclusionGlobals.cs)
- `WallOcclusionSettings.ConfigureMaterialMappings()` → `Array.Empty<Material>` (Assets/1.Scripts/Rendering/Occlusion/WallOcclusionSettings.cs)
- `RuntimeSceneServiceCoordinator.RestoreAll()` → `RestoreSuppressed` (Assets/1.Scripts/RuntimeSafety/RuntimeSceneServiceCoordinator.cs)
- `RuntimeSceneServiceCoordinator.GetScenePriority()` → `GetScenePriority` (Assets/1.Scripts/RuntimeSafety/RuntimeSceneServiceCoordinator.cs)
- `RuntimeSceneServiceCoordinator.RestoreSuppressed()` → `suppressed.Clear` (Assets/1.Scripts/RuntimeSafety/RuntimeSceneServiceCoordinator.cs)
- `RuntimeSceneServiceCoordinator.HandleSceneChanged()` → `Reconcile` (Assets/1.Scripts/RuntimeSafety/RuntimeSceneServiceCoordinator.cs)
- `RuntimeSceneServiceCoordinator.HandleSceneUnloaded()` → `Reconcile` (Assets/1.Scripts/RuntimeSafety/RuntimeSceneServiceCoordinator.cs)
- `UnreadableMeshColliderBakeScope.BeginLoadedScenes()` → `Begin` (Assets/1.Scripts/RuntimeSafety/UnreadableMeshColliderBakeScope.cs)
- `AudioManager.StopBGM()` → `BroAudio.Stop` (Assets/1.Scripts/Sound/AudioManager.cs)
- `AudioManager.InitVolumes()` → `System.Enum.GetValues` (Assets/1.Scripts/Sound/AudioManager.cs)
- `AudioManager.SetVolume()` → `BroAudio.SetVolume` (Assets/1.Scripts/Sound/AudioManager.cs)
- `AudioManager.SetMasterVolume()` → `BroAudio.SetVolume` (Assets/1.Scripts/Sound/AudioManager.cs)
- `AudioManager.GetVolume()` → `_volumes.TryGetValue` (Assets/1.Scripts/Sound/AudioManager.cs)
- `AudioManager.Pause()` → `BroAudio.Pause` (Assets/1.Scripts/Sound/AudioManager.cs)
- `AudioManager.UnPause()` → `BroAudio.UnPause` (Assets/1.Scripts/Sound/AudioManager.cs)
- `SceneBgmSwitcher.Start()` → `PlayForScene` (Assets/1.Scripts/Sound/SceneBgmSwitcher.cs)
- `SceneBgmSwitcher.OnSceneLoaded()` → `PlayForScene` (Assets/1.Scripts/Sound/SceneBgmSwitcher.cs)
- `VolumeSlider.Awake()` → `GetComponent<Slider>` (Assets/1.Scripts/Sound/VolumeSlider.cs)
- `VolumeSlider.OnDisable()` → `_slider.onValueChanged.RemoveListener` (Assets/1.Scripts/Sound/VolumeSlider.cs)
- `BossHealthHUD.OnDisable()` → `BindBoss` (Assets/1.Scripts/UI/Combat/BossHealthHUD.cs)
- `BossHudTarget.Awake()` → `GetComponent<Unit>` (Assets/1.Scripts/UI/Combat/BossHudTarget.cs)
- `BossHudTarget.OnNetworkDespawn()` → `base.OnNetworkDespawn` (Assets/1.Scripts/UI/Combat/BossHudTarget.cs)
- `BossHudTarget.OnDestroy()` → `base.OnDestroy` (Assets/1.Scripts/UI/Combat/BossHudTarget.cs)
- `CombatHUD.OnEnable()` → `Bind` (Assets/1.Scripts/UI/Combat/CombatHUD.cs)
- `DashCooldownHUD.Awake()` → `CacheSlotColors` (Assets/1.Scripts/UI/Combat/DashCooldownHUD.cs)
- `DashCooldownHUD.Bind()` → `Refresh` (Assets/1.Scripts/UI/Combat/DashCooldownHUD.cs)
- `DashCooldownHUD.Update()` → `Refresh` (Assets/1.Scripts/UI/Combat/DashCooldownHUD.cs)
- `FloatingDamagePopup.ForceRelease()` → `RequestRelease` (Assets/1.Scripts/UI/Combat/FloatingDamage/FloatingDamagePopup.cs)
- `FloatingDamagePresenter.Awake()` → `GetComponent<Unit>` (Assets/1.Scripts/UI/Combat/FloatingDamage/FloatingDamagePresenter.cs)
- `PassiveHUD.Bind()` → `Refresh` (Assets/1.Scripts/UI/Combat/PassiveHUD.cs)
- `PassiveHUD.Update()` → `Refresh` (Assets/1.Scripts/UI/Combat/PassiveHUD.cs)
- `PlayerCombatUiLifecyclePolicy.Awake()` → `CacheViews` (Assets/1.Scripts/UI/Combat/PlayerCombatUiLifecyclePolicy.cs)
- `PlayerCombatUiLifecyclePolicy.OnEnable()` → `Bind` (Assets/1.Scripts/UI/Combat/PlayerCombatUiLifecyclePolicy.cs)
- `PlayerCombatUiLifecyclePolicy.OnDisable()` → `UnbindLifeCycle` (Assets/1.Scripts/UI/Combat/PlayerCombatUiLifecyclePolicy.cs)
- `PlayerCombatUiLifecyclePolicy.HandleLifeStateChanged()` → `ApplyState` (Assets/1.Scripts/UI/Combat/PlayerCombatUiLifecyclePolicy.cs)
- `PlayerCombatUiLifecyclePolicy.ResolveLifeCycle()` → `player.GetComponent<PlayerLifeCycleController>` (Assets/1.Scripts/UI/Combat/PlayerCombatUiLifecyclePolicy.cs)
- `SkillCooldownHUD.Awake()` → `CacheSlotColors` (Assets/1.Scripts/UI/Combat/SkillCooldownHUD.cs)
- `SkillCooldownHUD.Bind()` → `Refresh` (Assets/1.Scripts/UI/Combat/SkillCooldownHUD.cs)
- `SkillCooldownHUD.Update()` → `Refresh` (Assets/1.Scripts/UI/Combat/SkillCooldownHUD.cs)
- `StatusEffectHUD.Bind()` → `Refresh` (Assets/1.Scripts/UI/Combat/StatusEffectHUD.cs)
- `StatusEffectHUD.Update()` → `Refresh` (Assets/1.Scripts/UI/Combat/StatusEffectHUD.cs)
- `UnitOverheadHealthBar.Awake()` → `GetComponentInParent<Player>` (Assets/1.Scripts/UI/Combat/UnitOverheadHealthBar.cs)
- `PersistentEventSystem.OnSceneLoaded()` → `RemoveForeignEventSystems` (Assets/1.Scripts/UI/PersistentEventSystem.cs)
- `PersistentEventSystem.RemoveForeignEventSystems()` → `Destroy` (Assets/1.Scripts/UI/PersistentEventSystem.cs)
- `ResultStatsView.Start()` → `Apply` (Assets/1.Scripts/UI/ResultStatsView.cs)
- `ResultStatsView.FindText()` → `GetComponentsInChildren<TMP_Text>` (Assets/1.Scripts/UI/ResultStatsView.cs)
- `UiModalBlocker.OnEnable()` → `UiInputGateManager.Acquire` (Assets/1.Scripts/UI/UiModalBlocker.cs)
- `UiModalBlocker.OnDisable()` → `UiInputGateManager.Release` (Assets/1.Scripts/UI/UiModalBlocker.cs)
- `HitFlash.Awake()` → `GetComponent<Unit>` (Assets/1.Scripts/Unit/HitFlash.cs)
- `Hurtbox.Awake()` → `ResolveOwner` (Assets/1.Scripts/Unit/Hurtbox.cs)
- `Hurtbox.OnValidate()` → `ResolveOwner` (Assets/1.Scripts/Unit/Hurtbox.cs)
- `Hurtbox.ResolveOwner()` → `ResolveReferences` (Assets/1.Scripts/Unit/Hurtbox.cs)
- `StatusEffectController.Awake()` → `GetComponent<PlayerEncounterLock>` (Assets/1.Scripts/Unit/StatusEffectController.cs)
- `StatusEffectController.GetStackCount()` → `Mathf.Max` (Assets/1.Scripts/Unit/StatusEffectController.cs)
- `StatusEffectController.Apply()` → `Apply` (Assets/1.Scripts/Unit/StatusEffectController.cs)
- `Unit.TakeDamage()` → `ApplyHealthDamage` (Assets/1.Scripts/Unit/Unit.cs)
- `Unit.TakeDamage()` → `TakeDamage` (Assets/1.Scripts/Unit/Unit.cs)
- `Unit.ApplyDirectHealthDamage()` → `ApplyHealthDamage` (Assets/1.Scripts/Unit/Unit.cs)
- `Unit.ApplyMaxHealthPercentDamage()` → `ApplyHealthDamage` (Assets/1.Scripts/Unit/Unit.cs)
- `Unit.ApplyCurrentHealthPercentDamage()` → `ApplyHealthDamage` (Assets/1.Scripts/Unit/Unit.cs)
- `Unit.GetStatMultiplier()` → `StatusFacade.GetStatMultiplier` (Assets/1.Scripts/Unit/Unit.cs)
- `BaseAttack.SetDamageSnapshot()` → `InitializeAttackInfo` (Assets/1.Scripts/Unit/Weapon/BaseAttack.cs)
- `BaseAttack.SetDamage()` → `Mathf.Max` (Assets/1.Scripts/Unit/Weapon/BaseAttack.cs)
- `BaseAttack.SetAttackType()` → `InitializeAttackInfo` (Assets/1.Scripts/Unit/Weapon/BaseAttack.cs)
- `BaseAttack.GetTargetName()` → `hurtbox.TryGetOwner` (Assets/1.Scripts/Unit/Weapon/BaseAttack.cs)
- `LinearKnockback.ApplyKnockbackClientRpc()` → `_rigidbody.AddForce` (Assets/1.Scripts/Unit/Weapon/LinearKnockback.cs)
- `OverlapAttack.Awake()` → `Mathf.Max` (Assets/1.Scripts/Unit/Weapon/OverlapAttack.cs)
- `BitMaskHelper.CheckEquals()` → `EqualityComparer<T>.Default.Equals` (Assets/1.Scripts/Utility/BitMaskHelper.cs)
- `Edit.Log()` → `Debug.Log` (Assets/1.Scripts/Utility/Edit.cs)
- `Edit.LogWarning()` → `Debug.LogWarning` (Assets/1.Scripts/Utility/Edit.cs)
- `Edit.LogError()` → `Debug.LogError` (Assets/1.Scripts/Utility/Edit.cs)
- `Edit.LogAssertion()` → `Debug.LogAssertion` (Assets/1.Scripts/Utility/Edit.cs)
- `UnityMcpBehaviorGraphTools.FindVariablesProperty()` → `serializedObject.FindProperty` (Assets/1.Scripts/Utility/Editor/UnityMcpBehaviorGraphTools.cs)
- `UnityMcpBehaviorGraphTools.FindAssetAtPath()` → `AssetDatabase.LoadAllAssetsAtPath` (Assets/1.Scripts/Utility/Editor/UnityMcpBehaviorGraphTools.cs)
- `ColliderMathUtility.Abs()` → `Mathf.Abs` (Assets/1.Scripts/Utility/Math/ColliderMathUtility.cs)
- `FirstPersonController.LateUpdate()` → `CameraRotation` (Assets/INab Studio/Demo Assets/Unity Companion License/StarterAssets/FirstPersonController/Scripts/FirstPersonController.cs)
- `CharacterEffect.PlayEffect_CharacterEffect()` → `SendPlayEvent` (Assets/INab Studio/Vfx Assets/Character Effects/Core/Scripts/CharacterEffect.cs)
- `CharacterEffect.StopEffect_CharacterEffect()` → `SendStopEvent` (Assets/INab Studio/Vfx Assets/Character Effects/Core/Scripts/CharacterEffect.cs)
- `CharacterEffectEditor.OnInspectorGUI()` → `base.OnInspectorGUI` (Assets/INab Studio/Vfx Assets/Character Effects/Core/Scripts/Editor/CharacterEffectEditor.cs)
- `CharacterEffectAPIShowcase.SetEffectPrefab1()` → `StartEffect` (Assets/INab Studio/Vfx Assets/Character Effects/Demo Files/CharacterEffectAPIShowcase.cs)
- `CharacterEffectAPIShowcase.SetEffectPrefab2()` → `StartEffect` (Assets/INab Studio/Vfx Assets/Character Effects/Demo Files/CharacterEffectAPIShowcase.cs)
- `ShowcaseSpawnerCharacterEffect.OnEnable()` → `PlayAll` (Assets/INab Studio/Vfx Assets/Character Effects/Demo Files/ShowcaseSpawnerCharacterEffect.cs)
- `ShowcaseSpawnerCharacterEffect.DestroyPrefabs()` → `spawnedObjects.Clear` (Assets/INab Studio/Vfx Assets/Character Effects/Demo Files/ShowcaseSpawnerCharacterEffect.cs)
- `ShowcaseSpawnerCharacterEffect.PlayAll()` → `obj.GetComponentsInChildren<CharacterEffect>` (Assets/INab Studio/Vfx Assets/Character Effects/Demo Files/ShowcaseSpawnerCharacterEffect.cs)
- `ShowcaseSpawnerCharacterEffect.StopAll()` → `obj.GetComponentsInChildren<CharacterEffect>` (Assets/INab Studio/Vfx Assets/Character Effects/Demo Files/ShowcaseSpawnerCharacterEffect.cs)
- `EditorUtilties.GetFoldoutState()` → `SessionState.GetBool` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.SetFoldoutState()` → `SessionState.SetBool` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.FoldoutGeneral()` → `GetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.SetFoldoutGeneral()` → `SetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.FoldoutEditorTesting()` → `GetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.SetFoldoutEditorTesting()` → `SetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.FoldoutEffectSettings()` → `GetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.AnimatorEffectSettings()` → `GetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.SetFoldoutEffectSettings()` → `SetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.SetAnimatorEffectSettings()` → `SetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.FoldoutMaterialsProperties()` → `GetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `EditorUtilties.SetFoldoutMaterialsProperties()` → `SetFoldoutState` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `LabeledSectionScope.Dispose()` → `EditorGUILayout.Space` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `FoldoutHeaderScope.Dispose()` → `EditorGUILayout.EndFoldoutHeaderGroup` (Assets/INab Studio/Vfx Assets/Common/Editor/EditorUtilties.cs)
- `UniformMeshSampleEditor.Setup()` → `EditorGUILayout.PropertyField` (Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/Editor/UniformMeshSampleEditor.cs)
- `UniformMeshSampleEditor.EffectsLoading()` → `EditorGUILayout.HelpBox` (Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/Editor/UniformMeshSampleEditor.cs)
- `UniformMeshBaker.SetGraphicsBuffer()` → `BindGraphicsBuffer` (Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/UniformMeshBaking.cs)
- `UniformMeshSample.SendPlayEvent()` → `vfxComponent?.Play` (Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/UniformMeshSample.cs)
- `UniformMeshSample.SendStopEvent()` → `vfxComponent?.Stop` (Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/UniformMeshSample.cs)
- `UniformMeshSample.Start()` → `SetupVfxGraph` (Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/UniformMeshSample.cs)
- `UniformMeshSample.OnDisable()` → `meshBaker.OnDisable` (Assets/INab Studio/Vfx Assets/Common/Scripts/Uniform Mesh/UniformMeshSample.cs)
- `VFXLossyTransformBinder.OnEnable()` → `UpdateSubProperties` (Assets/INab Studio/Vfx Assets/Common/Utilities/VFXLossyTransformBinder.cs)
- `VFXLossyTransformBinder.OnValidate()` → `UpdateSubProperties` (Assets/INab Studio/Vfx Assets/Common/Utilities/VFXLossyTransformBinder.cs)
- `VFXLossyTransformBinder.IsValid()` → `component.HasVector3` (Assets/INab Studio/Vfx Assets/Common/Utilities/VFXLossyTransformBinder.cs)
- `VFXLossyTransformBinder.ToString()` → `string.Format` (Assets/INab Studio/Vfx Assets/Common/Utilities/VFXLossyTransformBinder.cs)
- `WeaponTrailEffectEditor.OnSceneGUI()` → `ourTarget.DrawHandles` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/Editor/WeaponTrailEffectEditor.cs)
- `WeaponTrailEffectEditor.Setup()` → `EditorGUILayout.PropertyField` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/Editor/WeaponTrailEffectEditor.cs)
- `WeaponTrailEffectEditor.EffectsLoading()` → `EditorGUILayout.HelpBox` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/Editor/WeaponTrailEffectEditor.cs)
- `WeaponTrailEffect.EnsurePresetsForAllClips()` → `GetOrCreatePresetForClip` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs)
- `WeaponTrailEffect.EventSetTrailLength()` → `data.target.SetProperty_Length` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs)
- `WeaponTrailEffect.EventStartTrail()` → `data.target.StartTrail` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs)
- `WeaponTrailEffect.EventStopTrail()` → `data.target.StopTrail` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs)
- `WeaponTrailEffect.OnDisable()` → `DisposePreviewGraph` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs)
- `WeaponTrailEffect.OnDestroy()` → `DisposePreviewGraph` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs)
- `WeaponTrailEffect.SetTrailLength()` → `SetProperty_Length` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs)
- `WeaponTrailEffect.StartTrailWithLength()` → `StartTrail` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Core/Scripts/WeaponTrailEffect.cs)
- `TrailAPIShowcase.SetLengthPropertyWithSlider()` → `SetTrailLength` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/API Examples/TrailAPIShowcase.cs)
- `TrailAPIShowcase.SetTrailPrefab1()` → `StartTrail` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/API Examples/TrailAPIShowcase.cs)
- `TrailAPIShowcase.SetTrailPrefab2()` → `StartTrail` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/API Examples/TrailAPIShowcase.cs)
- `RuntimeAnimatorPlayer.Start()` → `FindAnimations` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/RuntimeAnimatorPlayer.cs)
- `RuntimeAnimatorPlayer.OnEnable()` → `FindAnimations` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/RuntimeAnimatorPlayer.cs)
- `ShowcaseAutoPlay.SetActiveCategory()` → `trailCategories[selectedClipIndex].SetActive` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseAutoPlay.cs)
- `ShowcaseAutoPlay.Start()` → `SetActiveCategory` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseAutoPlay.cs)
- `ShowcaseSpawnerTrail.OnEnable()` → `PlayAll` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `ShowcaseSpawnerTrail.OnValidate()` → `ChangleRotationSpeedAll` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `ShowcaseSpawnerTrail.DestroyPrefabs()` → `spawnedObjects.Clear` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `ShowcaseSpawnerTrail.PlayAll()` → `obj.GetComponent<WeaponTrailEffect>().SetTrailLength` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `ShowcaseSpawnerTrail.StopAll()` → `obj.GetComponent<WeaponTrailEffect>().StopTrail` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `ShowcaseSpawnerTrail.ChangleLengthAll()` → `obj.GetComponent<WeaponTrailEffect>().SetTrailLength` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `ShowcaseSpawnerTrail.ChangleRotationSpeedAll()` → `obj.GetComponent<RotateAroundAxisTrail>` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `ShowcaseSpawnerTrail.PauseAll()` → `obj.GetComponent<RotateAroundAxisTrail>` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `ShowcaseSpawnerTrail.GetPrefabsFromChildren()` → `obj.GetComponent<WeaponTrailEffect>` (Assets/INab Studio/Vfx Assets/Weapon FX Series/Weapon Trails FX/Demo Files/Other Scripts/ShowcaseSpawnerTrail.cs)
- `WallOcclusionRuntimeTests.BuildRange_NullSettingsDisablesFade()` → `Assert.That` (Assets/Tests/EditMode/Occlusion/WallOcclusionRuntimeTests.cs)
