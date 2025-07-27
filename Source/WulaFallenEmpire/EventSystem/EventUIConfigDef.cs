using UnityEngine;
using Verse;

namespace WulaFallenEmpire
{
    public class EventUIConfigDef : Def
    {
        // General Style
        public GameFont labelFont = GameFont.Small;
        public bool drawBorders = true;
        public string defaultBackgroundImagePath;

        // Virtual Layout Dimensions
        public Vector2 lihuiSize = new Vector2(500f, 800f);
        public Vector2 nameSize = new Vector2(260f, 130f);
        public Vector2 textSize = new Vector2(650f, 500f);
        public float optionsWidth = 610f;
        
        // Virtual Layout Offsets
        public float textNameOffset = 20f;
        public float optionsTextOffset = 20f;
    }
}
