﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mirle.AgvAseMiddler.Model.TransferSteps;

namespace Mirle.AgvAseMiddler.Controller.Handler.TransCmdsSteps
{
    [Serializable]
    public class Move : ITransferStatus
    {
        public void DoTransfer(MainFlowHandler mainFlowHandler)
        {
            TransferStep curTransferStep = mainFlowHandler.GetCurTransferStep();
            EnumTransferStepType type = curTransferStep.GetTransferStepType();

            switch (type)
            {
                case EnumTransferStepType.Move:
                case EnumTransferStepType.MoveToCharger:
                    MoveCmdInfo moveCmd = (MoveCmdInfo)curTransferStep;
                    if (moveCmd.MovingSections.Count > 0)
                    {
                        if (mainFlowHandler.StopCharge())
                        {
                            if (mainFlowHandler.IsOverrideMove)
                            {
                                if (mainFlowHandler.CallMoveControlOverride(moveCmd))
                                {
                                    mainFlowHandler.IsMoveEnd = false;
                                    mainFlowHandler.PrepareForAskingReserve(moveCmd);
                                }
                            }
                            else if (mainFlowHandler.IsAvoidMove)
                            {
                                if (mainFlowHandler.CallMoveControlAvoid(moveCmd))
                                {
                                    mainFlowHandler.IsMoveEnd = false;
                                    mainFlowHandler.PrepareForAskingReserve(moveCmd);
                                }
                            }
                            else
                            {
                                if (mainFlowHandler.CallMoveControlWork(moveCmd))
                                {
                                    mainFlowHandler.IsMoveEnd = false;
                                    mainFlowHandler.PrepareForAskingReserve(moveCmd);
                                }
                            }
                        }

                        break;
                    }
                    else
                    {
                        //原地移動
                        mainFlowHandler.MoveControl_OnMoveFinished(this, EnumMoveComplete.Success);
                        break;
                    }
                case EnumTransferStepType.Load:
                    mainFlowHandler.SetTransCmdsStep(new Load());
                    mainFlowHandler.DoTransfer();
                    break;
                case EnumTransferStepType.Unload:
                    mainFlowHandler.SetTransCmdsStep(new Unload());
                    mainFlowHandler.DoTransfer();
                    break;
                case EnumTransferStepType.Empty:
                default:
                    mainFlowHandler.SetTransCmdsStep(new Idle());
                    mainFlowHandler.DoTransfer();
                    break;
            }
        }

    }
}
