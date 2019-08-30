﻿using Mirle.Agv.Controller.Tools;
using Mirle.Agv.Model;
using Mirle.Agv.Model.Configs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mirle.Agv.Controller
{
    public class AgvMoveRevise
    {
        private ReviseParameter reviseParameter;
        private OntimeReviseConfig ontimeReviseConfig = null;
        private ElmoDriver elmoDriver = null;
        private List<Sr2000Driver> DriverSr2000List = null;
        private AlarmHandler alarmHandler;
        Dictionary<EnumMoveControlSafetyType, SafetyData> safety;
        private ComputeFunction computeFunction = new ComputeFunction();
        private LoggerAgent loggerAgent = LoggerAgent.Instance;
        private string device = "AgvMoveRevise";
        private uint lastCount = 0;
        private int lastSR2000Index = -1;

        public AgvMoveRevise(OntimeReviseConfig ontimeReviseConfig, ElmoDriver elmoDriver, List<Sr2000Driver> DriverSr2000List,
                             Dictionary<EnumMoveControlSafetyType, SafetyData> Safety, AlarmHandler alarmHandler)
        {
            this.alarmHandler = alarmHandler;
            this.ontimeReviseConfig = ontimeReviseConfig;
            this.elmoDriver = elmoDriver;
            this.DriverSr2000List = DriverSr2000List;
            safety = Safety;
            SettingReviseData(100, true);
        }

        public void SettingReviseData(double velocity, bool dirFlag)
        {
            reviseParameter = new ReviseParameter(ontimeReviseConfig, velocity, dirFlag);
        }

        private void WriteLog(string category, string logLevel, string device, string carrierId, string message,
                             [System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        {
            string classMethodName = GetType().Name + ":" + memberName;
            LogFormat logFormat = new LogFormat(category, logLevel, classMethodName, device, carrierId, message);

            loggerAgent.LogMsg(logFormat.Category, logFormat);
        }

        private void SendAlarmCode(int alarmCode)
        {
            if (alarmHandler == null)
                return;

            try
            {
                WriteLog("MoveControl", "3", device, "", "SetAlarm, alarmCode : " + alarmCode.ToString());
                alarmHandler.SetAlarm(alarmCode);
            }
            catch (Exception ex)
            {
                WriteLog("Error", "3", device, "", "SetAlarm失敗, Excption : " + ex.ToString());
            }
        }

        private bool LineRevise(ref double[] wheelTheta, double theta, double sectionDeviation, bool isOldCompute = true)
        {
            if ((reviseParameter.ReviseType == EnumLineReviseType.Theta || theta > reviseParameter.ModifyTheta || theta < -reviseParameter.ModifyTheta) &&
                sectionDeviation < reviseParameter.ModifySectionDeviation * ontimeReviseConfig.Return0ThetaPriority.SectionDeviation &&
                sectionDeviation > -reviseParameter.ModifySectionDeviation * ontimeReviseConfig.Return0ThetaPriority.SectionDeviation)
            {
                if ((theta < reviseParameter.ModifyTheta / ontimeReviseConfig.Return0ThetaPriority.Theta &&
                     theta > -reviseParameter.ModifyTheta / ontimeReviseConfig.Return0ThetaPriority.Theta))
                {
                    reviseParameter.ReviseType = EnumLineReviseType.None;
                    wheelTheta = new double[4] { 0, 0, 0, 0 };
                    return true;
                }
                else
                {
                    reviseParameter.ReviseType = EnumLineReviseType.Theta;
                    reviseParameter.ReviseValue = theta;
                    double turnTheta = theta / reviseParameter.ModifyTheta / ontimeReviseConfig.LinePriority.Theta * reviseParameter.MaxTheta;

                    if (turnTheta > reviseParameter.MaxTheta)
                        turnTheta = reviseParameter.MaxTheta;
                    else if (turnTheta < -reviseParameter.MaxTheta)
                        turnTheta = -reviseParameter.MaxTheta;

                    turnTheta = reviseParameter.DirFlag ? -turnTheta : turnTheta;
                    wheelTheta = new double[4] { turnTheta, turnTheta, -turnTheta, -turnTheta };
                    return true;
                }
            }
            else if (reviseParameter.ReviseType == EnumLineReviseType.SectionDeviation || sectionDeviation > reviseParameter.ModifySectionDeviation
                                                                                   || sectionDeviation < -reviseParameter.ModifySectionDeviation)
            {
                if (sectionDeviation < reviseParameter.ModifySectionDeviation / ontimeReviseConfig.Return0ThetaPriority.SectionDeviation &&
                    sectionDeviation > -reviseParameter.ModifySectionDeviation / ontimeReviseConfig.Return0ThetaPriority.SectionDeviation)
                {
                    reviseParameter.ReviseType = EnumLineReviseType.None;
                    wheelTheta = new double[4] { 0, 0, 0, 0 };
                    return true;
                }
                else
                {
                    reviseParameter.ReviseType = EnumLineReviseType.SectionDeviation;
                    reviseParameter.ReviseValue = sectionDeviation;
                    double turnTheta = sectionDeviation / reviseParameter.ModifySectionDeviation / ontimeReviseConfig.LinePriority.SectionDeviation * reviseParameter.MaxTheta;

                    if (turnTheta > reviseParameter.MaxTheta)
                        turnTheta = reviseParameter.MaxTheta;
                    else if (turnTheta < -reviseParameter.MaxTheta)
                        turnTheta = -reviseParameter.MaxTheta;

                    if (isOldCompute)
                        turnTheta = reviseParameter.DirFlag ? turnTheta : -turnTheta;

                    wheelTheta = new double[4] { turnTheta, turnTheta, turnTheta, turnTheta };
                    return true;
                }
            }
            else
            {
                reviseParameter.ReviseType = EnumLineReviseType.None;
                wheelTheta = new double[4] { 0, 0, 0, 0 };
                return true;
            }
        }

        private bool HorizontalRevise(ref double[] wheelTheta, double theta, double sectionDeviation, int wheelAngle, bool isOldCompute = true)
        {
            if (reviseParameter.ReviseType == EnumLineReviseType.SectionDeviation || sectionDeviation > reviseParameter.ModifySectionDeviation
                                                                                  || sectionDeviation < -reviseParameter.ModifySectionDeviation)
            {
                if (sectionDeviation < reviseParameter.ModifySectionDeviation / ontimeReviseConfig.Return0ThetaPriority.SectionDeviation &&
                    sectionDeviation > -reviseParameter.ModifySectionDeviation / ontimeReviseConfig.Return0ThetaPriority.SectionDeviation)
                {
                    reviseParameter.ReviseType = EnumLineReviseType.None;
                    wheelTheta = new double[4] { wheelAngle, wheelAngle, wheelAngle, wheelAngle };
                    return true;
                }
                else
                {
                    reviseParameter.ReviseType = EnumLineReviseType.SectionDeviation;
                    reviseParameter.ReviseValue = sectionDeviation;
                    double turnTheta = sectionDeviation / reviseParameter.ModifySectionDeviation / ontimeReviseConfig.LinePriority.SectionDeviation * reviseParameter.MaxTheta;

                    if (turnTheta > reviseParameter.MaxTheta)
                        turnTheta = reviseParameter.MaxTheta;
                    else if (turnTheta < -reviseParameter.MaxTheta)
                        turnTheta = -reviseParameter.MaxTheta;

                    if (isOldCompute)
                    {
                        turnTheta = reviseParameter.DirFlag ? turnTheta : -turnTheta;
                        turnTheta = (wheelAngle == -90) ? -turnTheta : turnTheta;
                    }

                    wheelTheta = new double[4] { wheelAngle + turnTheta, wheelAngle + turnTheta, wheelAngle + turnTheta, wheelAngle + turnTheta };
                    return true;
                }
            }
            else if ((reviseParameter.ReviseType == EnumLineReviseType.Theta || theta > reviseParameter.ModifyTheta || theta < -reviseParameter.ModifyTheta))
            {
                if ((theta < reviseParameter.ModifyTheta / ontimeReviseConfig.Return0ThetaPriority.Theta &&
                     theta > -reviseParameter.ModifyTheta / ontimeReviseConfig.Return0ThetaPriority.Theta))
                {
                    reviseParameter.ReviseType = EnumLineReviseType.None;
                    wheelTheta = new double[4] { wheelAngle, wheelAngle, wheelAngle, wheelAngle };
                    return true;
                }
                else
                {
                    reviseParameter.ReviseType = EnumLineReviseType.Theta;
                    reviseParameter.ReviseValue = theta;
                    double turnTheta = theta / reviseParameter.ModifyTheta / ontimeReviseConfig.LinePriority.Theta * reviseParameter.MaxTheta;

                    if (turnTheta > reviseParameter.MaxTheta)
                        turnTheta = reviseParameter.MaxTheta;
                    else if (turnTheta < -reviseParameter.MaxTheta)
                        turnTheta = -reviseParameter.MaxTheta;

                    turnTheta = reviseParameter.DirFlag ? -turnTheta : turnTheta;
                    wheelTheta = new double[4] { wheelAngle - turnTheta, wheelAngle + turnTheta, wheelAngle - turnTheta, wheelAngle + turnTheta };
                    return true;
                }
            }
            else
            {
                reviseParameter.ReviseType = EnumLineReviseType.None;
                wheelTheta = new double[4] { wheelAngle, wheelAngle, wheelAngle, wheelAngle };
                return true;
            }
        }
        
        private void UpdateParameter(double velocity)
        {
            if (velocity < 0)
                velocity = -velocity;

            if (reviseParameter.Velocity > velocity)
                velocity = reviseParameter.Velocity;

            if (velocity == reviseParameter.Velocity)
                return;

            reviseParameter.MaxTheta = 1;
            for (int i = 0; i < ontimeReviseConfig.SpeedToMaxTheta.Count; i++)
            {
                if (velocity < ontimeReviseConfig.SpeedToMaxTheta[i].Speed)
                {
                    reviseParameter.MaxTheta = ontimeReviseConfig.SpeedToMaxTheta[i].MaxTheta;
                    break;
                }
            }

            if (velocity > ontimeReviseConfig.MaxVelocity)
                velocity = ontimeReviseConfig.MaxVelocity;
            else if (velocity < ontimeReviseConfig.MinVelocity)
                velocity = ontimeReviseConfig.MinVelocity;

            reviseParameter.ModifyTheta = velocity / ontimeReviseConfig.ModifyPriority.Theta;
            reviseParameter.ModifySectionDeviation = velocity / ontimeReviseConfig.ModifyPriority.SectionDeviation;
            reviseParameter.ReviseType = EnumLineReviseType.None;
            reviseParameter.ReviseValue = 0;
            reviseParameter.ThetaCommandSpeed = 10;
        }

        public bool OntimeRevise(ref double[] wheelTheta, int wheelAngle, double velocity, ref string safetyMessage)
        {
            ThetaSectionDeviation reviseData = null;

            int index = -1;
            for (int i = 0; i < DriverSr2000List.Count; i++)
            {
                reviseData = DriverSr2000List[i].GetThetaSectionDeviation();
                if (reviseData != null)
                {
                    if (computeFunction.IsSameAngle(reviseData.BarcodeAngleInMap, reviseData.AGVAngleInMap, wheelAngle))
                    {
                        index = i;
                        break;
                    }
                    else
                        reviseData = null;
                }
            }

            if (safety != null && reviseData != null)
            {
                if (safety[EnumMoveControlSafetyType.OntimeReviseTheta].Enable)
                {
                    if (Math.Abs(reviseData.Theta) > safety[EnumMoveControlSafetyType.OntimeReviseTheta].Range)
                    {
                        safetyMessage = "角度偏差" + reviseData.Theta.ToString("0.0") +
                            "度,已超過安全設置的" +
                            safety[EnumMoveControlSafetyType.OntimeReviseTheta].Range.ToString("0.0") +
                            "度,因此啟動EMS!";

                        return true;
                    }
                }

                if (safety[EnumMoveControlSafetyType.OntimeReviseSectionDeviation].Enable)
                {
                    if (Math.Abs(reviseData.SectionDeviation) > safety[EnumMoveControlSafetyType.OntimeReviseSectionDeviation].Range)
                    {
                        safetyMessage = "軌道偏差" + reviseData.SectionDeviation.ToString("0") +
                            "mm,已超過安全設置的" +
                            safety[EnumMoveControlSafetyType.OntimeReviseSectionDeviation].Range.ToString("0") +
                            "mm,因此啟動EMS!";

                        return true;
                    }
                }
            }

            if (!elmoDriver.MoveCompelete(EnumAxis.GT))
                return false;

            if (reviseData == null)
            {
                wheelTheta = new double[4] { wheelAngle, wheelAngle, wheelAngle, wheelAngle };
                return true;
            }
            else
            {
                uint count = reviseData.Count;

                if (count == lastCount && index == lastSR2000Index)
                {
                    return false;
                }
                else
                {
                    lastCount = count;
                    lastSR2000Index = index;

                    UpdateParameter(velocity);

                    if (wheelAngle == 0)
                        return LineRevise(ref wheelTheta, reviseData.Theta, reviseData.SectionDeviation);
                    else
                        return HorizontalRevise(ref wheelTheta, reviseData.Theta, reviseData.SectionDeviation, wheelAngle);
                }
            }
        }
        
        public bool OntimeReviseByAGVPositionAndSection(ref double[] wheelTheta, int wheelAngle, double velocity, SectionLine section, ref string safetyMessage)
        {
            AGVPosition agvPosition = null;
            double theta = 0;
            double sectionDeviation = 0;

            int index = -1;
            for (int i = 0; i < DriverSr2000List.Count; i++)
            {
                agvPosition = DriverSr2000List[i].GetAGVPosition();

                if (agvPosition != null)
                {
                    if (computeFunction.IsSameAngle(agvPosition.BarcodeAngleInMap, agvPosition.AGVAngle, wheelAngle))
                    {
                        index = i;
                        break;
                    }
                    else
                        agvPosition = null;
                }
            }

            if (safety != null && agvPosition != null)
            {
                theta = agvPosition.AGVAngle + (reviseParameter.DirFlag ? 0 : 180) + wheelAngle - section.SectionAngle;

                while (theta > 180 || theta <= -180)
                {
                    if (theta > 180)
                        theta -= 360;
                    else if (theta <= -180)
                        theta += 360;
                }

                switch (section.SectionAngle)
                {
                    case 0:
                        sectionDeviation = agvPosition.Position.Y - section.Start.Y;
                        break;

                    case 180:
                        sectionDeviation = -(agvPosition.Position.Y - section.Start.Y);
                        break;

                    case 90:
                        sectionDeviation = agvPosition.Position.X - section.Start.X;
                        break;

                    case -90:
                        sectionDeviation = -(agvPosition.Position.X - section.Start.X);
                        break;

                    default:
                        safetyMessage = "Section 奇怪角度!";
                        return true;
                }

                if (safety[EnumMoveControlSafetyType.OntimeReviseTheta].Enable)
                {
                    if (Math.Abs(theta) > safety[EnumMoveControlSafetyType.OntimeReviseTheta].Range)
                    {
                        safetyMessage = "角度偏差" + theta.ToString("0.0") +
                            "度,已超過安全設置的" +
                            safety[EnumMoveControlSafetyType.OntimeReviseTheta].Range.ToString("0.0") +
                            "度,因此啟動EMS!";

                        return true;
                    }
                }

                if (safety[EnumMoveControlSafetyType.OntimeReviseSectionDeviation].Enable)
                {
                    if (Math.Abs(sectionDeviation) > safety[EnumMoveControlSafetyType.OntimeReviseSectionDeviation].Range)
                    {
                        safetyMessage = "軌道偏差" + sectionDeviation.ToString("0") +
                            "mm,已超過安全設置的" +
                            safety[EnumMoveControlSafetyType.OntimeReviseSectionDeviation].Range.ToString("0") +
                            "mm,因此啟動EMS!";

                        return true;
                    }
                }
            }

            if (!elmoDriver.MoveCompelete(EnumAxis.GT))
                return false;

            if (agvPosition == null)
            {
                wheelTheta = new double[4] { wheelAngle, wheelAngle, wheelAngle, wheelAngle };
                return true;
            }
            else
            {
                uint count = agvPosition.Count;

                if (count == lastCount && index == lastSR2000Index)
                {
                    return false;
                }
                else
                {
                    lastCount = count;
                    lastSR2000Index = index;

                    UpdateParameter(velocity);

                    if (wheelAngle == 0)
                        return LineRevise(ref wheelTheta, theta, sectionDeviation, false);
                    else
                        return HorizontalRevise(ref wheelTheta, theta, sectionDeviation, wheelAngle, false);
                }
            }
        }
    }
}
