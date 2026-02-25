using Verse;
using UnityEngine;

namespace WulaFallenEmpire
{
    public class PawnRenderNodeWorker_AttachmentBody_NoFlight : PawnRenderNodeWorker_AttachmentBody
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (parms.pawn.Flying)
            {
                return false;
            }
            return base.CanDrawNow(node, parms);
        }

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.ScaleFor(node, parms);
        }
    }
}