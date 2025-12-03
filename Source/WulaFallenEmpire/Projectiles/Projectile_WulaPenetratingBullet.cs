using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
  // 在 Wula_PathPierce_Extension 类中添加粒子特效相关的属性
  public class Wula_PathPierce_Extension : DefModExtension
  {
    // 原有的穿透属性
    public int maxHits = 3;
    public float damageFalloff = 0.25f;
    public bool preventFriendlyFire = false;
    public FleckDef tailFleckDef;
    public int fleckDelayTicks = 10;

    // 新增的击中特效属性（来自 Projectile_BulletWithEffect_Extension）
    public EffecterDef impactEffecter; // 击中时的特效
  }
  public class Projectile_WulaLineAttack : Bullet
  {
    private int hitCounter = 0;
    private List<Thing> alreadyDamaged = new List<Thing>();
    private Vector3 lastTickPosition;
    private int Fleck_MakeFleckTick;
    public int Fleck_MakeFleckTickMax = 1;
    public IntRange Fleck_MakeFleckNum = new IntRange(1, 1);
    public FloatRange Fleck_Angle = new FloatRange(-180f, 180f);
    public FloatRange Fleck_Scale = new FloatRange(1f, 1f);
    public FloatRange Fleck_Speed = new FloatRange(0f, 0f);
    public FloatRange Fleck_Rotation = new FloatRange(-180f, 180f);
    private Wula_PathPierce_Extension Props => def.GetModExtension<Wula_PathPierce_Extension>();
    public override void ExposeData()
    {
      base.ExposeData();
      Scribe_Values.Look(ref hitCounter, "hitCounter", 0);
      Scribe_Collections.Look(ref alreadyDamaged, "alreadyDamaged", LookMode.Reference);
      Scribe_Values.Look(ref lastTickPosition, "lastTickPosition");
      if (alreadyDamaged == null)
      {
        alreadyDamaged = new List<Thing>();
      }
    }
    public override void Launch(Thing launcher, Vector3 origin, LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, bool preventFriendlyFire = false, Thing equipment = null, ThingDef targetCoverDef = null)
    {
      base.Launch(launcher, origin, usedTarget, intendedTarget, hitFlags, preventFriendlyFire, equipment, targetCoverDef);
      this.lastTickPosition = origin;
      this.alreadyDamaged.Clear();
      this.hitCounter = 0;
      this.preventFriendlyFire = preventFriendlyFire || (Props?.preventFriendlyFire ?? false);
    }
    protected override void Tick()
    {
      Vector3 startPos = this.lastTickPosition;
      base.Tick();

      if (this.Destroyed) return;
      this.Fleck_MakeFleckTick++;
      if (this.Fleck_MakeFleckTick >= Props.fleckDelayTicks)
      {
        if (this.Fleck_MakeFleckTick >= (Props.fleckDelayTicks + this.Fleck_MakeFleckTickMax))
        {
          this.Fleck_MakeFleckTick = Props.fleckDelayTicks;
        }
        Map map = base.Map;
        int randomInRange = this.Fleck_MakeFleckNum.RandomInRange;
        Vector3 currentPosition = this.ExactPosition;
        for (int i = 0; i < randomInRange; i++)
        {
          float currentBulletAngle = ExactRotation.eulerAngles.y;
          float fleckRotationAngle = currentBulletAngle;
          float velocityAngle = this.Fleck_Angle.RandomInRange + currentBulletAngle;
          float randomInRange2 = this.Fleck_Scale.RandomInRange;
          float randomInRange3 = this.Fleck_Speed.RandomInRange;

          if (Props?.tailFleckDef != null)
          {
            FleckCreationData dataStatic = FleckMaker.GetDataStatic(currentPosition, map, Props.tailFleckDef, randomInRange2);
            dataStatic.rotation = fleckRotationAngle;
            dataStatic.rotationRate = this.Fleck_Rotation.RandomInRange;
            dataStatic.velocityAngle = velocityAngle;
            dataStatic.velocitySpeed = randomInRange3;
            map.flecks.CreateFleck(dataStatic);
          }
        }
      }
      if (this.Destroyed) return;
      Vector3 endPos = this.ExactPosition;

      CheckPathForDamage(startPos, endPos);
      this.lastTickPosition = endPos;
    }

    protected override void Impact(Thing hitThing, bool blockedByShield = false)
    {
      // 原有的穿透检测
      CheckPathForDamage(lastTickPosition, this.ExactPosition);

      if (hitThing != null && alreadyDamaged.Contains(hitThing))
      {
        base.Impact(null, blockedByShield);
      }
      else
      {
        base.Impact(hitThing, blockedByShield);
      }

      // 新增：触发击中特效（来自 Projectile_BulletWithEffect 的功能）
      if (Props?.impactEffecter != null)
      {
        // 创建一个新的 Effecter 实例并触发
        Effecter effecter = Props.impactEffecter.Spawn();
        effecter.Trigger(new TargetInfo(this.ExactPosition.ToIntVec3(), this.launcher.Map, false), this.launcher);

        // 可选：在一段时间后清理 Effecter
        // 这里我们使用一个临时的 Effecter，所以不需要手动清理
      }
    }
    private void CheckPathForDamage(Vector3 startPos, Vector3 endPos)
    {
      if (startPos == endPos) return;
      int maxHits = Props?.maxHits ?? 1;
      bool infinitePenetration = maxHits < 0;
      if (!infinitePenetration && hitCounter >= maxHits) return;
      Map map = this.Map;
      float distance = Vector3.Distance(startPos, endPos);
      Vector3 direction = (endPos - startPos).normalized;
      for (float i = 0; i < distance; i += 0.8f)
      {
        if (!infinitePenetration && hitCounter >= maxHits) break;
        Vector3 checkPos = startPos + direction * i;
        var thingsInCell = new HashSet<Thing>(map.thingGrid.ThingsListAt(checkPos.ToIntVec3()));
        foreach (Thing thing in thingsInCell)
        {
          if (thing is Pawn pawn && pawn != this.launcher && !alreadyDamaged.Contains(pawn))
          {
            bool shouldDamage = false;
            if (this.intendedTarget.Thing == pawn)
            {
              shouldDamage = true;
            }
            else if (pawn.HostileTo(this.launcher))
            {
              shouldDamage = true;
            }
            else if (!this.preventFriendlyFire)
            {
              shouldDamage = true;
            }
            if (shouldDamage)
            {
              ApplyPathDamage(pawn);
              if (!infinitePenetration && hitCounter >= maxHits) break;
            }
          }
        }
      }
    }
    private void ApplyPathDamage(Pawn pawn)
    {
      Wula_PathPierce_Extension props = Props;
      float falloff = props?.damageFalloff ?? 0.25f;

      float damageMultiplier = Mathf.Pow(1f - falloff, hitCounter);

      int damageAmount = (int)(this.DamageAmount * damageMultiplier);
      if (damageAmount <= 0) return;
      var dinfo = new DamageInfo(
          this.def.projectile.damageDef,
          damageAmount,
          this.ArmorPenetration * damageMultiplier,
          this.ExactRotation.eulerAngles.y,
          this.launcher,
          null,
          this.equipmentDef,
          DamageInfo.SourceCategory.ThingOrUnknown,
          this.intendedTarget.Thing);

      pawn.TakeDamage(dinfo);
      alreadyDamaged.Add(pawn);
      hitCounter++;
    }
  }
}