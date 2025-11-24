using RimWorld;
using Verse;

namespace WulaFallenEmpire
{
    public class CompProperties_PlaySoundOnSpawn : CompProperties
    {
        public SoundDef sound;
        
        // 可选：延迟播放声音（秒）
        public float delaySeconds = 0f;
        
        // 可选：只在特定条件下播放
        public bool onlyIfPlayerFaction = false;
        public bool onlyIfHostileFaction = false;
        public bool onlyIfNeutralFaction = false;
        
        // 可选：音量控制
        public float volume = 1f;
        public float pitch = 1f;
        
        // 可选：播放位置
        public bool playOnCamera = false; // 在摄像机位置播放
        public bool playAtThingPosition = true; // 在物体位置播放

        public CompProperties_PlaySoundOnSpawn()
        {
            compClass = typeof(CompPlaySoundOnSpawn);
        }
    }
}
