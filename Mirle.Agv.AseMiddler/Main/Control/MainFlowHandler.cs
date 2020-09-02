﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using com.mirle.aka.sc.ProtocolFormat.ase.agvMessage;
using com.mirle.iibg3k0.ttc.Common;

using Google.Protobuf.Collections;

using Mirle.Agv.AseMiddler.Model;
using Mirle.Agv.AseMiddler.Model.Configs;
using Mirle.Agv.AseMiddler.Model.TransferSteps;
using Mirle.Agv.AseMiddler.View;
using Mirle.Tools;
using Newtonsoft.Json;

namespace Mirle.Agv.AseMiddler.Controller
{
    public class MainFlowHandler
    {
        #region TransCmds
        private List<TransferStep> transferSteps = new List<TransferStep>();
        public List<TransferStep> PtransferSteps //200523 dabid+
        {
            get { return transferSteps; }
        }

        public bool GoNextTransferStep { get; set; }
        public int TransferStepsIndex { get; private set; } = 0;
        public bool IsOverrideMove { get; set; }
        public bool IsAvoidMove { get; set; }
        public bool IsArrivalCharge { get; set; } = false;

        #endregion

        #region Controller

        public AgvcConnector agvcConnector;
        public MirleLogger mirleLogger = null;
        public AlarmHandler alarmHandler;
        public MapHandler mapHandler;
        public AsePackage asePackage;
        public UserAgent UserAgent { get; set; } = new UserAgent();

        #endregion

        #region Threads

        private Thread thdVisitTransferSteps;
        public bool IsVisitTransferStepPause { get; set; } = false;

        private Thread thdTrackPosition;
        public bool IsTrackPositionPause { get; set; } = false;

        private Thread thdWatchChargeStage;
        public bool IsWatchChargeStagePause { get; set; } = false;

        #endregion

        #region Events
        public event EventHandler<InitialEventArgs> OnComponentIntialDoneEvent;

        #endregion

        #region Models
        public Vehicle Vehicle;
        private bool isIniOk;
        public int InitialSoc { get; set; } = 70;
        public bool IsFirstAhGet { get; set; }
        public string CanAutoMsg { get; set; } = "";
        public DateTime StartChargeTimeStamp { get; set; } = DateTime.Now;
        public DateTime StopChargeTimeStamp { get; set; } = DateTime.Now;
        public bool WaitingTransferCompleteEnd { get; set; } = false;
        public string DebugLogMsg { get; set; } = "";
        public LastIdlePosition LastIdlePosition { get; set; } = new LastIdlePosition();
        public bool IsLowPowerStartChargeTimeout { get; set; } = false;
        public int BatteryTimeoutCounter { get; set; } = 3;
        public bool IsStopChargTimeoutInRobotStep { get; set; } = false;
        public DateTime LowPowerStartChargeTimeStamp { get; set; } = DateTime.Now;
        public int LowPowerRepeatedlyChargeCounter { get; set; } = 0;
        public bool IsStopCharging { get; set; } = false;
        public int VisitTransferStepCounter { get; set; } = 0;

        #endregion

        public MainFlowHandler()
        {
            isIniOk = true;
        }

        #region InitialComponents

        public void InitialMainFlowHandler()
        {
            Vehicle = Vehicle.Instance;
            LoggersInitial();
            ConfigInitial();
            VehicleInitial();
            ControllersInitial();
            EventInitial();

            VehicleLocationInitialAndThreadsInitial();

            if (isIniOk)
            {
                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(true, "全部"));
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "Start Process Ok.");
            }
        }

        private void ConfigInitial()
        {
            try
            {
                //Main Configs 
                int minThreadSleep = 100;
                string allText = System.IO.File.ReadAllText("MainFlowConfig.json");
                Vehicle.MainFlowConfig = JsonConvert.DeserializeObject<MainFlowConfig>(allText);
                if (Vehicle.MainFlowConfig.IsSimulation)
                {
                    Vehicle.LoginLevel = EnumLoginLevel.Admin;
                }
                Vehicle.MainFlowConfig.VisitTransferStepsSleepTimeMs = Math.Max(Vehicle.MainFlowConfig.VisitTransferStepsSleepTimeMs, minThreadSleep);
                Vehicle.MainFlowConfig.TrackPositionSleepTimeMs = Math.Max(Vehicle.MainFlowConfig.TrackPositionSleepTimeMs, minThreadSleep);
                Vehicle.MainFlowConfig.WatchLowPowerSleepTimeMs = Math.Max(Vehicle.MainFlowConfig.WatchLowPowerSleepTimeMs, minThreadSleep);

                allText = System.IO.File.ReadAllText("MapConfig.json");
                Vehicle.MapConfig = JsonConvert.DeserializeObject<MapConfig>(allText);

                allText = System.IO.File.ReadAllText("AgvcConnectorConfig.json");
                Vehicle.AgvcConnectorConfig = JsonConvert.DeserializeObject<AgvcConnectorConfig>(allText);
                Vehicle.AgvcConnectorConfig.ScheduleIntervalMs = Math.Max(Vehicle.AgvcConnectorConfig.ScheduleIntervalMs, minThreadSleep);
                Vehicle.AgvcConnectorConfig.AskReserveIntervalMs = Math.Max(Vehicle.AgvcConnectorConfig.AskReserveIntervalMs, minThreadSleep);

                allText = System.IO.File.ReadAllText("AlarmConfig.json");
                Vehicle.AlarmConfig = JsonConvert.DeserializeObject<AlarmConfig>(allText);

                allText = System.IO.File.ReadAllText("BatteryLog.json");
                Vehicle.BatteryLog = JsonConvert.DeserializeObject<BatteryLog>(allText);
                InitialSoc = Vehicle.BatteryLog.InitialSoc;

                //AsePackage Configs
                allText = System.IO.File.ReadAllText("AsePackageConfig.json");
                Vehicle.AsePackageConfig = JsonConvert.DeserializeObject<AsePackageConfig>(allText);
                Vehicle.AsePackageConfig.ScheduleIntervalMs = Math.Max(Vehicle.AsePackageConfig.ScheduleIntervalMs, minThreadSleep);
                Vehicle.AsePackageConfig.WatchWifiSignalIntervalMs = Math.Max(Vehicle.AsePackageConfig.WatchWifiSignalIntervalMs, minThreadSleep);
                Vehicle.AseMoveConfig.WatchPositionInterval = Math.Max(Vehicle.AseMoveConfig.WatchPositionInterval, minThreadSleep);
                Vehicle.AseBatteryConfig.WatchBatteryStateIntervalInCharging = Math.Max(Vehicle.AseBatteryConfig.WatchBatteryStateIntervalInCharging, minThreadSleep);
                Vehicle.AseBatteryConfig.WatchBatteryStateInterval = Math.Max(Vehicle.AseBatteryConfig.WatchBatteryStateInterval, minThreadSleep);

                allText = System.IO.File.ReadAllText("PspConnectionConfig.json");
                Vehicle.PspConnectionConfig = JsonConvert.DeserializeObject<PspConnectionConfig>(allText);

                allText = System.IO.File.ReadAllText("AseBatteryConfig.json");
                Vehicle.AseBatteryConfig = JsonConvert.DeserializeObject<AseBatteryConfig>(allText);

                allText = System.IO.File.ReadAllText("AseMoveConfig.json");
                Vehicle.AseMoveConfig = JsonConvert.DeserializeObject<AseMoveConfig>(allText);

                if (Vehicle.MainFlowConfig.IsSimulation)
                {
                    Vehicle.AseBatteryConfig.WatchBatteryStateInterval = 30 * 1000;
                    Vehicle.AseBatteryConfig.WatchBatteryStateIntervalInCharging = 30 * 1000;
                    Vehicle.AseMoveConfig.WatchPositionInterval = 5000;
                }

                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(true, "讀寫設定檔"));
            }
            catch (Exception)
            {
                isIniOk = false;
                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(false, "讀寫設定檔"));
            }
        }

        private void LoggersInitial()
        {
            try
            {
                string loggerConfigPath = "Log.ini";
                if (File.Exists(loggerConfigPath))
                {
                    mirleLogger = MirleLogger.Instance;
                }
                else
                {
                    throw new Exception();
                }

                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(true, "紀錄器"));
            }
            catch (Exception)
            {
                isIniOk = false;
                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(false, "紀錄器缺少 Log.ini"));
            }
        }

        private void ControllersInitial()
        {
            try
            {
                alarmHandler = new AlarmHandler();
                mapHandler = new MapHandler();
                agvcConnector = new AgvcConnector(this);
                asePackage = new AsePackage();
                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(true, "控制層"));
            }
            catch (Exception ex)
            {
                isIniOk = false;
                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(false, "控制層"));
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void VehicleInitial()
        {
            try
            {
                IsFirstAhGet = Vehicle.MainFlowConfig.IsSimulation;

                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(true, "台車"));
            }
            catch (Exception ex)
            {
                isIniOk = false;
                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(false, "台車"));
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void EventInitial()
        {
            try
            {
                //來自middleAgent的NewTransCmds訊息, Send to MainFlow(this)'mapHandler
                agvcConnector.OnInstallTransferCommandEvent += AgvcConnector_OnInstallTransferCommandEvent;
                agvcConnector.OnOverrideCommandEvent += AgvcConnector_OnOverrideCommandEvent;
                agvcConnector.OnAvoideRequestEvent += AgvcConnector_OnAvoideRequestEvent;
                agvcConnector.OnLogMsgEvent += LogMsgHandler;
                agvcConnector.OnRenameCassetteIdEvent += AgvcConnector_OnRenameCassetteIdEvent;
                agvcConnector.OnStopClearAndResetEvent += AgvcConnector_OnStopClearAndResetEvent;


                agvcConnector.OnAgvcAcceptMoveArrivalEvent += AgvcConnector_OnAgvcAcceptMoveArrivalEvent;
                agvcConnector.OnAgvcAcceptLoadArrivalEvent += AgvcConnector_OnAgvcAcceptLoadArrivalEvent;
                agvcConnector.OnAgvcAcceptLoadCompleteEvent += AgvcConnector_OnAgvcAcceptLoadCompleteEvent;
                agvcConnector.OnAgvcAcceptBcrReadReply += AgvcConnector_OnAgvcContinueBcrReadEvent;
                agvcConnector.OnAgvcAcceptUnloadArrivalEvent += AgvcConnector_OnAgvcAcceptUnloadArrivalEvent;
                agvcConnector.OnAgvcAcceptUnloadCompleteEvent += AgvcConnector_OnAgvcAcceptUnloadCompleteEvent;
                agvcConnector.OnSendRecvTimeoutEvent += AgvcConnector_OnSendRecvTimeoutEvent;
                agvcConnector.OnCstRenameEvent += AgvcConnector_OnCstRenameEvent;

                //來自MoveControl的移動結束訊息, Send to MainFlow(this)'middleAgent'mapHandler
                asePackage.OnUpdateSlotStatusEvent += AsePackage_OnUpdateSlotStatusEvent;
                asePackage.OnModeChangeEvent += AsePackage_OnModeChangeEvent;
                asePackage.ImportantPspLog += AsePackage_ImportantPspLog;

                //來自IRobotControl的取放貨結束訊息, Send to MainFlow(this)'middleAgent'mapHandler
                asePackage.OnRobotInterlockErrorEvent += AsePackage_OnRobotInterlockErrorEvent;
                asePackage.OnRobotCommandFinishEvent += AsePackage_OnRobotCommandFinishEvent;
                asePackage.OnRobotCommandErrorEvent += AsePackage_OnRobotCommandErrorEvent;

                //來自IBatterysControl的電量改變訊息, Send to middleAgent
                asePackage.OnBatteryPercentageChangeEvent += agvcConnector.AseBatteryControl_OnBatteryPercentageChangeEvent;
                asePackage.OnBatteryPercentageChangeEvent += AseBatteryControl_OnBatteryPercentageChangeEvent;

                asePackage.OnStatusChangeReportEvent += AsePackage_OnStatusChangeReportEvent;

                asePackage.OnAlarmCodeSetEvent += AsePackage_OnAlarmCodeSetEvent1;
                asePackage.OnAlarmCodeResetEvent += AsePackage_OnAlarmCodeResetEvent;

                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(true, "事件"));
            }
            catch (Exception ex)
            {
                isIniOk = false;
                OnComponentIntialDoneEvent?.Invoke(this, new InitialEventArgs(false, "事件"));

                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void VehicleLocationInitialAndThreadsInitial()
        {
            if (Vehicle.MainFlowConfig.IsSimulation)
            {
                try
                {
                    AsePositionArgs positionArgs = new AsePositionArgs()
                    {
                        Arrival = EnumAseArrival.Arrival,
                        MapPosition = Vehicle.Mapinfo.addressMap.First(/*x => x.Key != ""*/).Value.Position
                    };
                    asePackage.ReceivePositionArgsQueue.Enqueue(positionArgs);
                }
                catch (Exception ex)
                {
                    Vehicle.AseMoveStatus.LastMapPosition = new MapPosition();
                    LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                }
            }
            else
            {
                if (Vehicle.IsLocalConnect)
                {
                    asePackage.AllAgvlStatusReportRequest();
                }
            }
            StartVisitTransferSteps();
            StartTrackPosition();
            StartWatchChargeStage();
            var msg = $"讀取到的電量為{Vehicle.BatteryLog.InitialSoc}";
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, msg);
        }

        #endregion

        #region Thd Visit TransferSteps
        private void VisitTransferSteps()
        {
            while (true)
            {
                if (IsVisitTransferStepPause || WaitingTransferCompleteEnd)
                {
                    //SpinWait.SpinUntil(() => false, Vehicle.MainFlowConfig.VisitTransferStepsSleepTimeMs);
                    Thread.Sleep(Vehicle.MainFlowConfig.VisitTransferStepsSleepTimeMs);
                    continue;
                }
                try
                {
                    VisitTransferStepCounter++;
                    if (GoNextTransferStep)
                    {
                        GoNextTransferStep = false;
                        if (TransferStepsIndex < 0)
                        {
                            TransferStepsIndex = 0;
                        }
                        if (transferSteps.Count == 0)
                        {
                            if (Vehicle.AgvcTransCmdBuffer.Count > 0)
                            {
                                if (!Vehicle.IsOptimize)
                                    GetTransferCommandToDo();
                            }
                            else
                            {
                                transferSteps = new List<TransferStep> { new EmptyTransferStep() };
                                //transferSteps.Add(new EmptyTransferStep());
                            }
                        }

                        if (TransferStepsIndex < transferSteps.Count)
                        {
                            DoTransferStep(transferSteps[TransferStepsIndex]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                    Thread.Sleep(1);
                }

                Thread.Sleep(Vehicle.MainFlowConfig.VisitTransferStepsSleepTimeMs);
                //SpinWait.SpinUntil(() => false, Vehicle.MainFlowConfig.VisitTransferStepsSleepTimeMs);
            }
        }

        private void GetTransferCommandToDo()
        {
            transferSteps = new List<TransferStep>();
            TransferStepsIndex = 0;

            if (Vehicle.AgvcTransCmdBuffer.Count > 1)
            {
                if (!Vehicle.MainFlowConfig.DualCommandOptimize)
                {
                    List<AgvcTransCmd> agvcTransCmds = Vehicle.AgvcTransCmdBuffer.Values.ToList();
                    AgvcTransCmd onlyCmd = agvcTransCmds[0];

                    switch (onlyCmd.EnrouteState)
                    {
                        case CommandState.None:
                            TransferStepsAddMoveCmdInfo(onlyCmd.UnloadAddressId, onlyCmd.CommandId);
                            transferSteps.Add(new EmptyTransferStep());
                            break;
                        case CommandState.LoadEnroute:
                            TransferStepsAddMoveCmdInfo(onlyCmd.LoadAddressId, onlyCmd.CommandId);
                            TransferStepsAddLoadCmdInfo(onlyCmd);
                            break;
                        case CommandState.UnloadEnroute:
                            TransferStepsAddMoveCmdInfo(onlyCmd.UnloadAddressId, onlyCmd.CommandId);
                            TransferStepsAddUnloadCmdInfo(onlyCmd);
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    List<AgvcTransCmd> agvcTransCmds = Vehicle.AgvcTransCmdBuffer.Values.ToList();
                    AgvcTransCmd cmd001 = agvcTransCmds[0];

                    switch (cmd001.EnrouteState)
                    {
                        case CommandState.None:
                            {
                                TransferStepsAddMoveCmdInfo(cmd001.UnloadAddressId, cmd001.CommandId);
                                transferSteps.Add(new EmptyTransferStep());
                            }
                            break;
                        case CommandState.LoadEnroute:
                            {
                                var disCmd001Load = DistanceFromLastPosition(cmd001.LoadAddressId);

                                AgvcTransCmd cmd002 = agvcTransCmds[1];
                                if (cmd002.EnrouteState == CommandState.LoadEnroute)
                                {
                                    var disCmd002Load = DistanceFromLastPosition(cmd002.LoadAddressId);
                                    if (disCmd001Load <= disCmd002Load)
                                    {
                                        TransferStepsAddMoveCmdInfo(cmd001.LoadAddressId, cmd001.CommandId);
                                        TransferStepsAddLoadCmdInfo(cmd001);
                                    }
                                    else
                                    {
                                        TransferStepsAddMoveCmdInfo(cmd002.LoadAddressId, cmd002.CommandId);
                                        TransferStepsAddLoadCmdInfo(cmd002);
                                    }
                                }
                                else if (cmd002.EnrouteState == CommandState.UnloadEnroute)
                                {
                                    var disCmd002Unload = DistanceFromLastPosition(cmd002.UnloadAddressId);
                                    if (disCmd001Load <= disCmd002Unload)
                                    {
                                        TransferStepsAddMoveCmdInfo(cmd001.LoadAddressId, cmd001.CommandId);
                                        TransferStepsAddLoadCmdInfo(cmd001);
                                    }
                                    else
                                    {
                                        TransferStepsAddMoveCmdInfo(cmd002.UnloadAddressId, cmd002.CommandId);
                                        TransferStepsAddUnloadCmdInfo(cmd002);
                                    }
                                }
                                else
                                {
                                    StopClearAndReset();
                                    SetAlarmFromAgvm(1);
                                }
                            }
                            break;
                        case CommandState.UnloadEnroute:
                            {
                                var disCmd001Unload = DistanceFromLastPosition(cmd001.UnloadAddressId);

                                AgvcTransCmd cmd002 = agvcTransCmds[1];
                                if (cmd002.EnrouteState == CommandState.LoadEnroute)
                                {
                                    var disCmd002Load = DistanceFromLastPosition(cmd002.LoadAddressId);
                                    if (disCmd001Unload <= disCmd002Load)
                                    {
                                        TransferStepsAddMoveCmdInfo(cmd001.UnloadAddressId, cmd001.CommandId);
                                        TransferStepsAddUnloadCmdInfo(cmd001);
                                    }
                                    else
                                    {
                                        TransferStepsAddMoveCmdInfo(cmd002.LoadAddressId, cmd002.CommandId);
                                        TransferStepsAddLoadCmdInfo(cmd002);
                                    }
                                }
                                else if (cmd002.EnrouteState == CommandState.UnloadEnroute)
                                {
                                    var disCmd002Unload = DistanceFromLastPosition(cmd002.UnloadAddressId);
                                    if (disCmd001Unload <= disCmd002Unload)
                                    {
                                        TransferStepsAddMoveCmdInfo(cmd001.UnloadAddressId, cmd001.CommandId);
                                        TransferStepsAddUnloadCmdInfo(cmd001);
                                    }
                                    else
                                    {
                                        TransferStepsAddMoveCmdInfo(cmd002.UnloadAddressId, cmd002.CommandId);
                                        TransferStepsAddUnloadCmdInfo(cmd002);
                                    }
                                }
                                else
                                {
                                    StopClearAndReset();
                                    SetAlarmFromAgvm(1);
                                }
                            }
                            break;
                        default:
                            break;
                    }

                }
            }
            else if (Vehicle.AgvcTransCmdBuffer.Count == 1)
            {
                List<AgvcTransCmd> agvcTransCmds = Vehicle.AgvcTransCmdBuffer.Values.ToList();
                AgvcTransCmd onlyCmd = agvcTransCmds[0];

                switch (onlyCmd.EnrouteState)
                {
                    case CommandState.None:
                        TransferStepsAddMoveCmdInfo(onlyCmd.UnloadAddressId, onlyCmd.CommandId);
                        transferSteps.Add(new EmptyTransferStep());
                        break;
                    case CommandState.LoadEnroute:
                        TransferStepsAddMoveCmdInfo(onlyCmd.LoadAddressId, onlyCmd.CommandId);
                        TransferStepsAddLoadCmdInfo(onlyCmd);
                        break;
                    case CommandState.UnloadEnroute:
                        TransferStepsAddMoveCmdInfo(onlyCmd.UnloadAddressId, onlyCmd.CommandId);
                        TransferStepsAddUnloadCmdInfo(onlyCmd);
                        break;
                    default:
                        break;
                }
            }

        }

        private void DoTransferStep(TransferStep transferStep)
        {
            switch (transferStep.GetTransferStepType())
            {
                case EnumTransferStepType.Move:
                case EnumTransferStepType.MoveToCharger:
                    MoveCmdInfo moveCmdInfo = (MoveCmdInfo)transferStep;
                    if (moveCmdInfo.EndAddress.Id != Vehicle.AseMoveStatus.LastAddress.Id || Vehicle.IsReAuto)
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[移動前 斷充] Move Stop Charge");
                        StopCharge();
                    }

                    if (moveCmdInfo.EndAddress.Id == Vehicle.AseMoveStatus.LastAddress.Id)
                    {
                        if (Vehicle.MainFlowConfig.IsSimulation)
                        {
                            AseMoveControl_OnMoveFinished(this, EnumMoveComplete.Success);
                        }
                        else
                        {
                            if (Vehicle.IsReAuto)
                            {
                                Vehicle.AseMoveStatus.IsMoveEnd = false;
                                Vehicle.IsReAuto = false;
                                if (IsAvoidMove)
                                {
                                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[重新 二次定位] Same address move end.");
                                    asePackage.PartMove(EnumAseMoveCommandIsEnd.End);
                                }
                                else
                                {
                                    var cmdId = moveCmdInfo.CmdId;
                                    var transferCommand = Vehicle.AgvcTransCmdBuffer[cmdId];
                                    if (Vehicle.AgvcTransCmdBuffer.Count == 0)
                                    {
                                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[重新 二次定位] Same address move end.");
                                        asePackage.PartMove(EnumAseMoveCommandIsEnd.End);
                                    }
                                    else if (IsLoadUnloadType(transferCommand))
                                    {
                                        switch (transferCommand.SlotNumber)
                                        {
                                            case EnumSlotNumber.L:
                                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[重新 二次定位 左開蓋] Same address move end. Open slot left.");
                                                asePackage.PartMove(EnumAseMoveCommandIsEnd.End, EnumSlotSelect.Left);
                                                break;
                                            case EnumSlotNumber.R:
                                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[重新 二次定位 右開蓋] Same address move end. Open slot right.");
                                                asePackage.PartMove(EnumAseMoveCommandIsEnd.End, EnumSlotSelect.Right);
                                                break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[原地移動 到站] Same address arrival.");

                                AseMoveControl_OnMoveFinished(this, EnumMoveComplete.Success);
                            }
                        }
                    }
                    else
                    {
                        Vehicle.AseMovingGuide.CommandId = moveCmdInfo.CmdId;
                        agvcConnector.ReportSectionPass();
                        if (!Vehicle.IsCharging)
                        {
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[退出 站點] Move Begin.");
                            Vehicle.AseMoveStatus.IsMoveEnd = false;
                            asePackage.PartMove(EnumAseMoveCommandIsEnd.Begin);

                            agvcConnector.ClearAllReserve();
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[詢問 路線] AskGuideAddressesAndSections.");
                            agvcConnector.AskGuideAddressesAndSections(moveCmdInfo);
                        }
                    }
                    break;
                case EnumTransferStepType.Load:
                    Load((LoadCmdInfo)transferStep);
                    break;
                case EnumTransferStepType.Unload:
                    Unload((UnloadCmdInfo)transferStep);
                    break;
                case EnumTransferStepType.Empty:
                    CheckTransferSteps();
                    break;
                default:
                    break;
            }
        }

        private static bool IsLoadUnloadType(AgvcTransCmd transferCommand)
        {
            return transferCommand.AgvcTransCommandType == EnumAgvcTransCommandType.Load || transferCommand.AgvcTransCommandType == EnumAgvcTransCommandType.Unload || transferCommand.AgvcTransCommandType == EnumAgvcTransCommandType.LoadUnload;
        }

        private void CheckTransferSteps()
        {
            if (TransferStepsIndex + 1 < transferSteps.Count)
            {
                transferSteps.RemoveAt(TransferStepsIndex);
            }

            GoNextTransferStep = true;
        }

        public void StartVisitTransferSteps()
        {
            TransferStepsIndex = 0;
            thdVisitTransferSteps = new Thread(VisitTransferSteps);
            thdVisitTransferSteps.IsBackground = true;
            thdVisitTransferSteps.Start();

            var msg = $"MainFlow : StartVisitTransferSteps";
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, msg);
        }

        public void PauseVisitTransferSteps()
        {
            IsVisitTransferStepPause = true;
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"MainFlow : PauseVisitTransferSteps");
        }

        public void ResumeVisitTransferSteps()
        {
            IsVisitTransferStepPause = false;
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"MainFlow : ResumeVisitTransferSteps");
        }

        public void ClearTransferSteps(string cmdId)
        {
            List<TransferStep> tempTransferSteps = new List<TransferStep>();
            foreach (var step in transferSteps)
            {
                if (step.CmdId != cmdId)
                {
                    tempTransferSteps.Add(step);
                }
            }
            transferSteps = tempTransferSteps;

            var msg = $"MainFlow : Clear Finished Command[{cmdId}]";
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, msg);
        }

        private void PauseTransfer()
        {
            agvcConnector.PauseAskReserve();
            PauseVisitTransferSteps();
        }

        private void ResumeTransfer()
        {
            ResumeVisitTransferSteps();
            agvcConnector.ResumeAskReserve();
        }

        #region Handle Transfer Command

        private static object installTransferCommandLocker = new object();

        private void AgvcConnector_OnInstallTransferCommandEvent(object sender, AgvcTransCmd agvcTransCmd)
        {
            lock (installTransferCommandLocker)
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[檢查搬送命令] Check Transfer Command [{agvcTransCmd.CommandId}]");

                #region 檢查搬送Command
                try
                {
                    switch (agvcTransCmd.AgvcTransCommandType)
                    {
                        case EnumAgvcTransCommandType.Move:
                        case EnumAgvcTransCommandType.MoveToCharger:
                            CheckVehicleIdle();
                            CheckMoveEndAddress(agvcTransCmd.UnloadAddressId);
                            break;
                        case EnumAgvcTransCommandType.Load:
                            CheckLoadPortAddress(agvcTransCmd.LoadAddressId);
                            CheckCstIdDuplicate(agvcTransCmd.CassetteId);
                            agvcTransCmd.SlotNumber = CheckVehicleSlot();
                            break;
                        case EnumAgvcTransCommandType.Unload:
                            CheckUnloadPortAddress(agvcTransCmd.UnloadAddressId);
                            agvcTransCmd.SlotNumber = CheckUnloadCstId(agvcTransCmd.CassetteId);
                            break;
                        case EnumAgvcTransCommandType.LoadUnload:
                            CheckLoadPortAddress(agvcTransCmd.LoadAddressId);
                            CheckUnloadPortAddress(agvcTransCmd.UnloadAddressId);
                            CheckCstIdDuplicate(agvcTransCmd.CassetteId);
                            agvcTransCmd.SlotNumber = CheckVehicleSlot();
                            break;
                        case EnumAgvcTransCommandType.Override:
                            CheckOverrideAddress(agvcTransCmd);
                            break;
                        case EnumAgvcTransCommandType.Else:
                            break;
                        default:
                            break;
                    }

                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[檢查搬送命令 成功] Check Transfer Command Ok. [{agvcTransCmd.CommandId}]");

                }
                catch (Exception ex)
                {
                    LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                    agvcConnector.ReplyTransferCommand(agvcTransCmd.CommandId, agvcTransCmd.GetCommandActionType(), agvcTransCmd.SeqNum, 1, ex.Message);
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[檢查搬送命令 失敗] Check Transfer Command Fail. [{agvcTransCmd.CommandId}] {ex.Message}");
                    return;
                }
                #endregion

                #region 搬送流程更新
                try
                {
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[初始化搬送命令] Initial Transfer Command [{agvcTransCmd.CommandId}]");

                    PauseTransfer();
                    Vehicle.AgvcTransCmdBuffer.TryAdd(agvcTransCmd.CommandId, agvcTransCmd);
                    agvcConnector.Commanding();
                    agvcConnector.ReplyTransferCommand(agvcTransCmd.CommandId, agvcTransCmd.GetCommandActionType(), agvcTransCmd.SeqNum, 0, "");
                    asePackage.SetTransferCommandInfoRequest(agvcTransCmd, EnumCommandInfoStep.Begin);
                    if (Vehicle.AgvcTransCmdBuffer.Count == 1)
                    {
                        InitialTransferSteps(agvcTransCmd);
                        GoNextTransferStep = true;
                    }
                    if (!Vehicle.IsPause())
                    {
                        ResumeTransfer();
                    }

                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[初始化搬送命令 成功] Initial Transfer Command Ok. [{agvcTransCmd.CommandId}]");
                }
                catch (Exception ex)
                {
                    agvcConnector.ReplyTransferCommand(agvcTransCmd.CommandId, agvcTransCmd.GetCommandActionType(), agvcTransCmd.SeqNum, 1, "");
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[初始化搬送命令 失敗] Initial Transfer Command Fail. [{agvcTransCmd.CommandId}]");
                    LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                }
                #endregion
            }
        }

        private void CheckVehicleIdle()
        {
            if (WaitingTransferCompleteEnd)
            {
                throw new Exception("Vehicle is waiting last transfer commmand end.");
            }

            if (!IsVehicleIdle())
            {
                throw new Exception("Vehicle is not idle.");
            }
        }

        private void CheckCstIdDuplicate(string cassetteId)
        {
            var agvcTransCmdBuffer = Vehicle.AgvcTransCmdBuffer.Values.ToList();
            for (int i = 0; i < agvcTransCmdBuffer.Count; i++)
            {
                if (agvcTransCmdBuffer[i].CassetteId == cassetteId)
                {
                    throw new Exception("Transfer command casette ID duplicate.");
                }
            }
        }

        private EnumSlotNumber CheckVehicleSlot()
        {
            if (IsVehicleDoingMoveCommand())
            {
                throw new Exception("Vehicle has move command, can not do loadunload.");
            }

            var leftSlotState = Vehicle.AseCarrierSlotL.CarrierSlotStatus;
            var rightSlotState = Vehicle.AseCarrierSlotR.CarrierSlotStatus;
            var agvcTransCmdBuffer = Vehicle.AgvcTransCmdBuffer.Values.ToList();

            switch (Vehicle.MainFlowConfig.SlotDisable)
            {
                case EnumSlotSelect.None:
                    {
                        if (leftSlotState != EnumAseCarrierSlotStatus.Empty && rightSlotState != EnumAseCarrierSlotStatus.Empty)
                        {
                            throw new Exception("Vehicle has no Slot to load.");
                        }
                        else if (Vehicle.AgvcTransCmdBuffer.Count >= 2)
                        {
                            throw new Exception("Vehicle has two other transfer command. Vehicle has no Slot to load.");
                        }
                        else if (Vehicle.AgvcTransCmdBuffer.Count == 1)
                        {
                            var curCmd = Vehicle.AgvcTransCmdBuffer.Values.First();
                            switch (curCmd.EnrouteState)
                            {

                                case CommandState.LoadEnroute:
                                case CommandState.UnloadEnroute:
                                    if (curCmd.SlotNumber == EnumSlotNumber.L)
                                    {
                                        if (rightSlotState != EnumAseCarrierSlotStatus.Empty)
                                        {
                                            throw new Exception("Vehicle has no Slot to load.");
                                        }
                                        else
                                        {
                                            return EnumSlotNumber.R;
                                        }
                                    }
                                    else
                                    {
                                        if (leftSlotState != EnumAseCarrierSlotStatus.Empty)
                                        {
                                            throw new Exception("Vehicle has no Slot to load.");
                                        }
                                        else
                                        {
                                            return EnumSlotNumber.L;
                                        }
                                    }
                                case CommandState.None:
                                default:
                                    if (leftSlotState == EnumAseCarrierSlotStatus.Empty)
                                    {
                                        return EnumSlotNumber.L;
                                    }
                                    else
                                    {
                                        return EnumSlotNumber.R;
                                    }
                            }
                        }
                        else
                        {
                            if (leftSlotState == EnumAseCarrierSlotStatus.Empty)
                            {
                                return EnumSlotNumber.L;
                            }
                            else
                            {
                                return EnumSlotNumber.R;
                            }
                        }
                    }
                case EnumSlotSelect.Left:
                    {
                        if (Vehicle.AgvcTransCmdBuffer.Count >= 2)
                        {
                            throw new Exception("Vehicle has two other transfer command. Vehicle has no Slot to load.");
                        }
                        else
                        {
                            if (rightSlotState != EnumAseCarrierSlotStatus.Empty)
                            {
                                throw new Exception("Left Slot is disable. Vehicle has no Slot to load.");

                            }
                            else
                            {
                                return EnumSlotNumber.R;
                            }
                        }
                    }
                case EnumSlotSelect.Right:
                    {
                        if (Vehicle.AgvcTransCmdBuffer.Count >= 2)
                        {
                            throw new Exception("Vehicle has two other transfer command. Vehicle has no Slot to load.");
                        }
                        else
                        {
                            if (leftSlotState != EnumAseCarrierSlotStatus.Empty)
                            {
                                throw new Exception("Right Slot is disable. Vehicle has no Slot to load.");
                            }
                            else
                            {
                                return EnumSlotNumber.L;
                            }
                        }
                    }
                case EnumSlotSelect.Both:
                    throw new Exception("Both Slot is disable. Can not do transfer command.");
                default:
                    {
                        if (leftSlotState != EnumAseCarrierSlotStatus.Empty && rightSlotState != EnumAseCarrierSlotStatus.Empty)
                        {
                            throw new Exception("Vehicle has no Slot to load.");
                        }
                        else if (Vehicle.AgvcTransCmdBuffer.Count >= 2)
                        {
                            throw new Exception("Vehicle has two other transfer command. Vehicle has no Slot to load.");
                        }
                        else if (Vehicle.AgvcTransCmdBuffer.Count == 1)
                        {
                            var curCmd = Vehicle.AgvcTransCmdBuffer.Values.First();
                            switch (curCmd.EnrouteState)
                            {

                                case CommandState.LoadEnroute:
                                case CommandState.UnloadEnroute:
                                    if (curCmd.SlotNumber == EnumSlotNumber.L)
                                    {
                                        if (rightSlotState != EnumAseCarrierSlotStatus.Empty)
                                        {
                                            throw new Exception("Vehicle has no Slot to load.");
                                        }
                                        else
                                        {
                                            return EnumSlotNumber.R;
                                        }
                                    }
                                    else
                                    {
                                        if (leftSlotState != EnumAseCarrierSlotStatus.Empty)
                                        {
                                            throw new Exception("Vehicle has no Slot to load.");
                                        }
                                        else
                                        {
                                            return EnumSlotNumber.L;
                                        }
                                    }
                                case CommandState.None:
                                default:
                                    if (leftSlotState == EnumAseCarrierSlotStatus.Empty)
                                    {
                                        return EnumSlotNumber.L;
                                    }
                                    else
                                    {
                                        return EnumSlotNumber.R;
                                    }
                            }
                        }
                        else
                        {
                            if (leftSlotState == EnumAseCarrierSlotStatus.Empty)
                            {
                                return EnumSlotNumber.L;
                            }
                            else
                            {
                                return EnumSlotNumber.R;
                            }
                        }
                    }
            }

        }

        private bool IsVehicleDoingMoveCommand()
        {
            if (Vehicle.AgvcTransCmdBuffer.Any())
            {
                var firstCommandType = Vehicle.AgvcTransCmdBuffer.Values.First().AgvcTransCommandType;
                return firstCommandType == EnumAgvcTransCommandType.Move || firstCommandType == EnumAgvcTransCommandType.MoveToCharger;
            }
            return false;
        }

        private EnumSlotNumber CheckUnloadCstId(string cassetteId)
        {
            if (IsVehicleDoingMoveCommand())
            {
                throw new Exception("Vehicle has move command, can not do loadunload.");
            }
            if (string.IsNullOrEmpty(cassetteId))
            {
                throw new Exception($"Unload CST ID is Empty.");
            }
            if (Vehicle.AseCarrierSlotL.CarrierId.Trim() == cassetteId)
            {
                return EnumSlotNumber.L;
            }
            else if (Vehicle.AseCarrierSlotR.CarrierId.Trim() == cassetteId)
            {
                return EnumSlotNumber.R;
            }
            else
            {
                throw new Exception($"No [{cassetteId}] to unload.");
            }
        }

        private void CheckOverrideAddress(AgvcTransCmd agvcTransCmd)
        {
            return;
        }

        private void CheckUnloadPortAddress(string unloadAddressId)
        {
            CheckMoveEndAddress(unloadAddressId);
            MapAddress unloadAddress = Vehicle.Mapinfo.addressMap[unloadAddressId];
            if (!unloadAddress.IsTransferPort())
            {
                throw new Exception($"{unloadAddressId} can not unload.");
            }
        }

        private void CheckLoadPortAddress(string loadAddressId)
        {
            CheckMoveEndAddress(loadAddressId);
            MapAddress loadAddress = Vehicle.Mapinfo.addressMap[loadAddressId];
            if (!loadAddress.IsTransferPort())
            {
                throw new Exception($"{loadAddressId} can not load.");
            }
        }

        private void CheckMoveEndAddress(string unloadAddressId)
        {
            if (!Vehicle.Mapinfo.addressMap.ContainsKey(unloadAddressId))
            {
                throw new Exception($"{unloadAddressId} is not in the map.");
            }
        }

        private void AgvcConnector_OnOverrideCommandEvent(object sender, AgvcOverrideCmd agvcOverrideCmd)
        {
            var msg = $"MainFlow :  Get [ Override ]Command[{agvcOverrideCmd.CommandId}],  start check .";
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, msg);
        }

        private void AgvcConnector_OnAvoideRequestEvent(object sender, AseMovingGuide aseMovingGuide)
        {
            #region 避車檢查
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"MainFlow :  Get Avoid Command, End Adr=[{aseMovingGuide.ToAddressId}],  start check .");

                agvcConnector.PauseAskReserve();

                if (Vehicle.AgvcTransCmdBuffer.Count == 0)
                {
                    throw new Exception("Vehicle has no Command, can not Avoid");
                }

                if (!IsMoveStep())
                {
                    throw new Exception("Vehicle is not moving, can not Avoid");
                }

                if (!IsMoveStopByNoReserve() && !Vehicle.AseMovingGuide.IsAvoidComplete)
                {
                    throw new Exception($"Vehicle is not stop by no reserve, can not Avoid");
                }

                //if (!IsAvoidCommandMatchTheMap(agvcMoveCmd))
                //{
                //    var reason = "避車路徑中 Port Adr 與路段不合圖資";
                //    RejectAvoidCommandAndResume(000018, reason, agvcMoveCmd);
                //    return;
                //}
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                RejectAvoidCommandAndResume(000036, ex.Message, aseMovingGuide);
            }
            #endregion

            #region 避車Command生成
            try
            {
                IsAvoidMove = true;
                agvcConnector.ClearAllReserve();
                Vehicle.AseMovingGuide = aseMovingGuide;
                SetupAseMovingGuideMovingSections();
                agvcConnector.SetupNeedReserveSections();
                agvcConnector.ReplyAvoidCommand(aseMovingGuide.SeqNum, 0, "");
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"MainFlow : Get 避車Command checked , 終點[{aseMovingGuide.ToAddressId}].");
                agvcConnector.ResumeAskReserve();
            }
            catch (Exception ex)
            {
                StopClearAndReset();
                var reason = "避車Exception";
                RejectAvoidCommandAndResume(000036, reason, aseMovingGuide);
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }

            #endregion
        }

        private bool IsMoveStopByNoReserve()
        {
            return Vehicle.AseMovingGuide.ReserveStop == VhStopSingle.On;
        }

        private void RejectAvoidCommandAndResume(int alarmCode, string reason, AseMovingGuide aseMovingGuide)
        {
            try
            {
                SetAlarmFromAgvm(alarmCode);
                agvcConnector.ReplyAvoidCommand(aseMovingGuide.SeqNum, 1, reason);
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, string.Concat($"MainFlow : Reject Avoid Command, ", reason));
                agvcConnector.ResumeAskReserve();
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion

        #region Convert AgvcTransferCommand to TransferSteps

        private void InitialTransferSteps(AgvcTransCmd agvcTransCmd)
        {
            TransferStepsIndex = 0;

            switch (agvcTransCmd.AgvcTransCommandType)
            {
                case EnumAgvcTransCommandType.Move:
                    TransferStepsAddMoveCmdInfo(agvcTransCmd.UnloadAddressId, agvcTransCmd.CommandId);
                    transferSteps.Add(new EmptyTransferStep());
                    break;
                case EnumAgvcTransCommandType.Load:
                    TransferStepsAddMoveCmdInfo(agvcTransCmd.LoadAddressId, agvcTransCmd.CommandId);
                    TransferStepsAddLoadCmdInfo(agvcTransCmd);
                    break;
                case EnumAgvcTransCommandType.Unload:
                    TransferStepsAddMoveCmdInfo(agvcTransCmd.UnloadAddressId, agvcTransCmd.CommandId);
                    TransferStepsAddUnloadCmdInfo(agvcTransCmd);
                    break;
                case EnumAgvcTransCommandType.LoadUnload:
                    TransferStepsAddMoveCmdInfo(agvcTransCmd.LoadAddressId, agvcTransCmd.CommandId);
                    TransferStepsAddLoadCmdInfo(agvcTransCmd);
                    break;
                case EnumAgvcTransCommandType.MoveToCharger:
                    TransferStepsAddMoveToChargerCmdInfo(agvcTransCmd.UnloadAddressId, agvcTransCmd.CommandId);
                    transferSteps.Add(new EmptyTransferStep());
                    break;
                case EnumAgvcTransCommandType.Override:
                case EnumAgvcTransCommandType.Else:
                default:
                    break;
            }
        }

        private void TransferStepsAddUnloadCmdInfo(AgvcTransCmd agvcTransCmd)
        {
            UnloadCmdInfo unloadCmdInfo = new UnloadCmdInfo(agvcTransCmd);
            MapAddress unloadAddress = Vehicle.Mapinfo.addressMap[unloadCmdInfo.PortAddressId];
            unloadCmdInfo.GateType = unloadAddress.GateType;
            if (string.IsNullOrEmpty(agvcTransCmd.UnloadPortId) || !unloadAddress.PortIdMap.ContainsKey(agvcTransCmd.UnloadPortId))
            {
                unloadCmdInfo.PortNumber = "1";
            }
            else
            {
                unloadCmdInfo.PortNumber = unloadAddress.PortIdMap[agvcTransCmd.UnloadPortId];
            }
            transferSteps.Add(unloadCmdInfo);
        }

        private void TransferStepsAddLoadCmdInfo(AgvcTransCmd agvcTransCmd)
        {
            LoadCmdInfo loadCmdInfo = new LoadCmdInfo(agvcTransCmd);
            MapAddress loadAddress = Vehicle.Mapinfo.addressMap[loadCmdInfo.PortAddressId];
            loadCmdInfo.GateType = loadAddress.GateType;
            if (string.IsNullOrEmpty(agvcTransCmd.LoadPortId) || !loadAddress.PortIdMap.ContainsKey(agvcTransCmd.LoadPortId))
            {
                loadCmdInfo.PortNumber = "1";
            }
            else
            {
                loadCmdInfo.PortNumber = loadAddress.PortIdMap[agvcTransCmd.LoadPortId];
            }
            transferSteps.Add(loadCmdInfo);
        }

        private void TransferStepsAddMoveCmdInfo(string endAddressId, string cmdId)
        {
            MapAddress endAddress = Vehicle.Mapinfo.addressMap[endAddressId];
            MoveCmdInfo moveCmd = new MoveCmdInfo(endAddress, cmdId);
            transferSteps.Add(moveCmd);
        }

        private void TransferStepsAddMoveToChargerCmdInfo(string endAddressId, string cmdId)
        {
            MapAddress endAddress = Vehicle.Mapinfo.addressMap[endAddressId];
            MoveToChargerCmdInfo moveCmd = new MoveToChargerCmdInfo(endAddress, cmdId);
            transferSteps.Add(moveCmd);
        }

        #endregion

        #endregion

        #region Thd Watch Charge Stage

        private void WatchChargeStage()
        {
            while (true)
            {
                try
                {
                    Vehicle.VehicleIdle = IsVehicleIdle();//200824 dabid+
                    Vehicle.LowPower = IsLowPower();//200824 dabid+stop
                    Vehicle.LowPowerStartChargeTimeout = IsLowPowerStartChargeTimeout;//200824 dabid+
                    if (Vehicle.AutoState == EnumAutoState.Auto && IsVehicleIdle() && !Vehicle.IsOptimize && IsLowPower() && !IsLowPowerStartChargeTimeout)
                    {
                        LowPowerStartCharge(Vehicle.AseMoveStatus.LastAddress);
                    }
                    if (Vehicle.AseBatteryStatus.Percentage < Vehicle.MainFlowConfig.LowPowerPercentage - 11 && !Vehicle.IsCharging) //200701 dabid+
                    {
                        SetAlarmFromAgvm(2);
                    }
                }
                catch (Exception ex)
                {
                    LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                    Thread.Sleep(1);
                }

                Thread.Sleep(Vehicle.MainFlowConfig.WatchLowPowerSleepTimeMs);
                //SpinWait.SpinUntil(() => false, Vehicle.MainFlowConfig.WatchLowPowerSleepTimeMs);
            }
        }

        private bool IsVehicleIdle()
        {
            if (transferSteps.Count == 0)
            {
                return true;
            }
            else if (transferSteps.Count == 1)
            {
                if (GetCurrentTransferStepType() == EnumTransferStepType.Empty)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public void StartWatchChargeStage()
        {
            thdWatchChargeStage = new Thread(WatchChargeStage);
            thdWatchChargeStage.IsBackground = true;
            thdWatchChargeStage.Start();
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"StartWatchChargeStage");
        }

        private bool IsLowPower()
        {
            return Vehicle.AseBatteryStatus.Percentage <= Vehicle.MainFlowConfig.LowPowerPercentage;
        }

        private bool IsHighPower()
        {
            return Vehicle.AseBatteryStatus.Percentage >= Vehicle.MainFlowConfig.HighPowerPercentage;
        }

        public void MainFormStartCharge()
        {
            StartCharge(Vehicle.AseMoveStatus.LastAddress);
        }

        private void StartCharge(MapAddress endAddress, int chargeTimeSec = -1)
        {
            try
            {
                IsArrivalCharge = true;
                var address = endAddress;
                var percentage = Vehicle.AseBatteryStatus.Percentage;
                var highPercentage = Vehicle.MainFlowConfig.HighPowerPercentage;

                if (address.IsCharger())
                {
                    if (IsHighPower())
                    {
                        var msg = $"Vehicle arrival {address.Id},Charge Direction = {address.ChargeDirection},Precentage = {percentage} > {highPercentage}(High Threshold),  thus NOT send charge command.";
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, msg);
                        return;
                    }

                    agvcConnector.ChargHandshaking();
                    Vehicle.IsCharging = true;

                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $@"Start Charge, Vehicle arrival {address.Id},Charge Direction = {address.ChargeDirection},Precentage = {percentage}.");

                    if (Vehicle.MainFlowConfig.IsSimulation)
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "[充電 成功] Start Charge success.");
                        if (chargeTimeSec > 0)
                        {
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $@"[提早斷充 開始({chargeTimeSec})後] Roboting STOP charge in ({chargeTimeSec}) sec.");
                            SpinWait.SpinUntil(() => false, chargeTimeSec * 1000);
                            StopCharge();
                        }
                        return;
                    }
                    Vehicle.CheckStartChargeReplyEnd = false;
                    asePackage.StartCharge(address.ChargeDirection);


                    if (chargeTimeSec > 0)
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $@"[提早斷充 開始({chargeTimeSec})後] Roboting STOP charge in ({chargeTimeSec}) sec.");
                        SpinWait.SpinUntil(() => false, chargeTimeSec * 1000);
                        StopCharge();
                    }
                    else
                    {
                        SpinWait.SpinUntil(() => Vehicle.CheckStartChargeReplyEnd, 30 * 1000);

                        if (Vehicle.CheckStartChargeReplyEnd)
                        {
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "[充電 成功] Start Charge success.");
                            agvcConnector.Charging();
                            IsLowPowerStartChargeTimeout = false;
                        }
                        else
                        {
                            Vehicle.IsCharging = false;
                            SetAlarmFromAgvm(000013);
                            asePackage.ChargeStatusRequest();
                            SpinWait.SpinUntil(() => false, 500);
                            asePackage.StopCharge();
                        }
                    }

                    Vehicle.CheckStartChargeReplyEnd = true;
                    IsArrivalCharge = false;
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                IsArrivalCharge = false;
            }
        }

        private void LowPowerStartCharge(MapAddress lastAddress)
        {
            try
            {
                Vehicle.ArrivalCharge = IsArrivalCharge;//200824 dabid for Watch Not AUTO Charge
                if (IsArrivalCharge) return;

                var address = lastAddress;
                var percentage = Vehicle.AseBatteryStatus.Percentage;
                var pos = Vehicle.AseMoveStatus.LastMapPosition;
                Vehicle.IsCharger = address.IsCharger();//200824 dabid for Watch Not AUTO Charge
                if (address.IsCharger())
                {
                    if (Vehicle.IsCharging)
                    {
                        return;
                    }
                    else
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[低電量閒置 自動充電] Addr = {address.Id},Precentage = {percentage} < {Vehicle.MainFlowConfig.LowPowerPercentage}(Low Threshold).");
                    }

                    if ((DateTime.Now - LowPowerStartChargeTimeStamp).TotalSeconds >= Vehicle.MainFlowConfig.LowPowerRepeatChargeIntervalSec)
                    {
                        LowPowerStartChargeTimeStamp = DateTime.Now;
                        LowPowerRepeatedlyChargeCounter = 0;
                    }
                    else
                    {
                        LowPowerRepeatedlyChargeCounter++;
                        if (LowPowerRepeatedlyChargeCounter > Vehicle.MainFlowConfig.LowPowerRepeatedlyChargeCounterMax)
                        {
                            Task.Run(() =>
                            {
                                IsLowPowerStartChargeTimeout = true;
                                SpinWait.SpinUntil(() => false, Vehicle.MainFlowConfig.SleepLowPowerWatcherSec * 1000);
                                IsLowPowerStartChargeTimeout = false;
                            });
                            Vehicle.LowPowerRepeatedlyChargeCounter = LowPowerRepeatedlyChargeCounter;
                            return;
                        }
                    }
                    agvcConnector.ChargHandshaking();

                    Vehicle.IsCharging = true;

                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $@"Start Charge, Vehicle arrival {address.Id},Charge Direction = {address.ChargeDirection},Precentage = {percentage}.");

                    if (Vehicle.MainFlowConfig.IsSimulation) return;

                    Vehicle.CheckStartChargeReplyEnd = false;

                    int retryCount = Vehicle.MainFlowConfig.ChargeRetryTimes;

                    for (int i = 0; i < retryCount; i++)
                    {
                        asePackage.StartCharge(address.ChargeDirection);

                        SpinWait.SpinUntil(() => Vehicle.CheckStartChargeReplyEnd, Vehicle.MainFlowConfig.StartChargeWaitingTimeoutMs);

                        if (Vehicle.CheckStartChargeReplyEnd)
                        {
                            break;
                        }
                    }

                    if (Vehicle.CheckStartChargeReplyEnd)
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "Start Charge success.");
                        agvcConnector.Charging();
                        //IsLowPowerStartChargeTimeout = false;
                    }
                    else
                    {
                        Vehicle.IsCharging = false;
                        SetAlarmFromAgvm(000013);
                        asePackage.ChargeStatusRequest();
                        SpinWait.SpinUntil(() => false, 500);
                        asePackage.StopCharge();
                        //IsLowPowerStartChargeTimeout = true;
                    }

                    Vehicle.CheckStartChargeReplyEnd = true;
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public void StopCharge()
        {
            try
            {
                //if (IsStopCharging) return;
                IsStopCharging = true;

                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $@"[斷充 開始] ]Try STOP charge.[IsCharging = {Vehicle.IsCharging}]");

                AseMoveStatus moveStatus = new AseMoveStatus(Vehicle.AseMoveStatus);
                var address = moveStatus.LastAddress;
                var pos = moveStatus.LastMapPosition;
                if (address.IsCharger())
                {
                    agvcConnector.ChargHandshaking();

                    if (Vehicle.MainFlowConfig.IsSimulation)
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[斷充 成功] Stop Charge success.");
                        Vehicle.IsCharging = false;
                        return;
                    }

                    //in starting charge
                    if (!Vehicle.CheckStartChargeReplyEnd) Thread.Sleep(Vehicle.MainFlowConfig.StopChargeWaitingTimeoutMs);

                    int retryCount = Vehicle.MainFlowConfig.DischargeRetryTimes;
                    Vehicle.IsCharging = true;

                    for (int i = 0; i < retryCount; i++)
                    {
                        asePackage.StopCharge();

                        SpinWait.SpinUntil(() => !Vehicle.IsCharging, Vehicle.MainFlowConfig.StopChargeWaitingTimeoutMs);

                        asePackage.ChargeStatusRequest();
                        SpinWait.SpinUntil(() => false, 500);

                        if (!Vehicle.IsCharging)
                        {
                            break;
                        }
                    }

                    if (!Vehicle.IsCharging)
                    {
                        agvcConnector.ChargeOff();
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[斷充 成功] Stop Charge success.");
                    }
                    else
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[斷充 逾時] Stop Charge Timeout.");
                        if (IsRobCommand(GetCurTransferStep()))
                        {
                            IsStopChargTimeoutInRobotStep = true;
                        }
                        else
                        {
                            SetAlarmFromAgvm(000014);
                            //AsePackage_OnModeChangeEvent(this, EnumAutoState.Manual);
                        }
                    }
                }
                IsStopCharging = false;
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                IsStopCharging = false;
            }
        }

        #endregion

        #region Thd Track Position

        private void TrackPosition()
        {
            while (true)
            {
                try
                {
                    //if (IsTrackPositionPause)
                    //{
                    //    SpinWait.SpinUntil(() => !IsTrackPositionPause, Vehicle.MainFlowConfig.TrackPositionSleepTimeMs);
                    //    continue;
                    //}

                    if (asePackage.ReceivePositionArgsQueue.Any())
                    {
                        asePackage.ReceivePositionArgsQueue.TryDequeue(out AsePositionArgs positionArgs);
                        DealAsePositionArgs(positionArgs);
                    }
                }
                catch (Exception ex)
                {
                    LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                    Thread.Sleep(1);
                }

                Thread.Sleep(Vehicle.MainFlowConfig.TrackPositionSleepTimeMs);
                //SpinWait.SpinUntil(() => false, Vehicle.MainFlowConfig.TrackPositionSleepTimeMs);
            }
        }

        private void DealAsePositionArgs(AsePositionArgs positionArgs)
        {
            try
            {
                AseMovingGuide movingGuide = new AseMovingGuide(Vehicle.AseMovingGuide);
                AseMoveStatus moveStatus = new AseMoveStatus(Vehicle.AseMoveStatus);
                moveStatus.LastMapPosition = positionArgs.MapPosition;
                CheckPositionUnchangeTimeout(positionArgs);

                if (movingGuide.GuideSectionIds.Any())
                {
                    if (positionArgs.Arrival == EnumAseArrival.EndArrival)
                    {
                        moveStatus.NearlyAddress = Vehicle.Mapinfo.addressMap[movingGuide.ToAddressId];
                        moveStatus.NearlySection = movingGuide.MovingSections.Last();
                        moveStatus.NearlySection.VehicleDistanceSinceHead = moveStatus.NearlySection.HeadAddress.MyDistance(moveStatus.NearlyAddress.Position);
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"Update Position. [Section = {moveStatus.NearlySection.Id}][Address = {moveStatus.NearlyAddress.Id}].");
                    }
                    else
                    {
                        var nearlyDistance = 999999;
                        foreach (string addressId in movingGuide.GuideAddressIds)
                        {
                            MapAddress mapAddress = Vehicle.Mapinfo.addressMap[addressId];
                            var dis = moveStatus.LastMapPosition.MyDistance(mapAddress.Position);

                            if (dis < nearlyDistance)
                            {
                                nearlyDistance = dis;
                                moveStatus.NearlyAddress = mapAddress;
                            }
                        }

                        foreach (string sectionId in movingGuide.GuideSectionIds)
                        {
                            MapSection mapSection = Vehicle.Mapinfo.sectionMap[sectionId];
                            if (mapSection.InSection(moveStatus.LastAddress.Id))
                            {
                                moveStatus.NearlySection = mapSection;
                            }
                        }
                        moveStatus.NearlySection.VehicleDistanceSinceHead = moveStatus.NearlyAddress.MyDistance(moveStatus.NearlySection.HeadAddress.Position);

                        if (moveStatus.NearlyAddress.Id != moveStatus.LastAddress.Id)
                        {
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"Update Position. [Section = {moveStatus.NearlySection.Id}][Address = {moveStatus.NearlyAddress.Id}].");
                        }
                    }

                    moveStatus.LastAddress = moveStatus.NearlyAddress;
                    moveStatus.LastSection = moveStatus.NearlySection;
                    moveStatus.HeadDirection = positionArgs.HeadAngle;
                    moveStatus.MovingDirection = positionArgs.MovingDirection;
                    moveStatus.Speed = positionArgs.Speed;
                    Vehicle.AseMoveStatus = moveStatus;
                    agvcConnector.ReportSectionPass();

                    UpdateMovePassSections(moveStatus.LastSection.Id);

                    for (int i = 0; i < movingGuide.MovingSections.Count; i++)
                    {
                        if (movingGuide.MovingSections[i].Id == moveStatus.LastSection.Id)
                        {
                            Vehicle.AseMovingGuide.MovingSectionsIndex = i;
                        }
                    }
                }
                else
                {
                    moveStatus.NearlyAddress = Vehicle.Mapinfo.addressMap.Values.ToList().OrderBy(address => address.MyDistance(positionArgs.MapPosition)).First();
                    moveStatus.NearlySection = Vehicle.Mapinfo.sectionMap.Values.ToList().FirstOrDefault(section => section.InSection(moveStatus.NearlyAddress));
                    moveStatus.NearlySection.VehicleDistanceSinceHead = moveStatus.NearlySection.HeadAddress.MyDistance(positionArgs.MapPosition);
                    moveStatus.LastMapPosition = positionArgs.MapPosition;
                    if (moveStatus.NearlyAddress.Id != moveStatus.LastAddress.Id)
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"Update Position. [LastSection = {moveStatus.LastSection.Id}][LastAddress = {moveStatus.LastAddress.Id}] to [NearlySection = {moveStatus.NearlySection.Id}][NearlyAddress = {moveStatus.NearlyAddress.Id}]");
                    }
                    moveStatus.LastAddress = moveStatus.NearlyAddress;
                    moveStatus.LastSection = moveStatus.NearlySection;
                    moveStatus.HeadDirection = positionArgs.HeadAngle;
                    moveStatus.MovingDirection = positionArgs.MovingDirection;
                    moveStatus.Speed = positionArgs.Speed;
                    Vehicle.AseMoveStatus = moveStatus;
                    agvcConnector.ReportSectionPass();
                }

                switch (positionArgs.Arrival)
                {
                    case EnumAseArrival.Fail:
                        AseMoveControl_OnMoveFinished(this, EnumMoveComplete.Fail);
                        break;
                    case EnumAseArrival.Arrival:
                        break;
                    case EnumAseArrival.EndArrival:
                        AseMoveControl_OnMoveFinished(this, EnumMoveComplete.Success);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void CheckPositionUnchangeTimeout(AsePositionArgs positionArgs)
        {
            if (!Vehicle.AseMoveStatus.IsMoveEnd)
            {
                if (LastIdlePosition.Position.MyDistance(positionArgs.MapPosition) <= Vehicle.MainFlowConfig.IdleReportRangeMm)
                {
                    if ((DateTime.Now - LastIdlePosition.TimeStamp).TotalMilliseconds >= Vehicle.MainFlowConfig.IdleReportIntervalMs)
                    {
                        UpdateLastIdlePositionAndTimeStamp(positionArgs);
                        SetAlarmFromAgvm(55);
                    }
                }
                else
                {
                    UpdateLastIdlePositionAndTimeStamp(positionArgs);
                }
            }
            else
            {
                LastIdlePosition.TimeStamp = DateTime.Now;
            }
        }

        private void UpdateLastIdlePositionAndTimeStamp(AsePositionArgs positionArgs)
        {
            LastIdlePosition lastIdlePosition = new LastIdlePosition();
            lastIdlePosition.Position = positionArgs.MapPosition;
            lastIdlePosition.TimeStamp = DateTime.Now;
            LastIdlePosition = lastIdlePosition;
        }

        public void StartTrackPosition()
        {
            thdTrackPosition = new Thread(TrackPosition);
            thdTrackPosition.IsBackground = true;
            thdTrackPosition.Start();
        }

        #endregion

        public bool CanVehMove()
        {
            try
            {
                if (Vehicle.IsCharging) //dabid
                {
                    StopCharge();
                }
            }
            catch
            {

            }
            //Vehicle.TMP_IsHome = Vehicle.AseRobotStatus.IsHome; //200828 dabid for Watch Not AskAllSectionsReserveInOnce
            //Vehicle.TMP_IsCharging = Vehicle.IsCharging;//!//200828 dabid for Watch Not AskAllSectionsReserveInOnce
            return Vehicle.AseRobotStatus.IsHome && !Vehicle.IsCharging;

        }

        public void AgvcConnector_GetReserveOkUpdateMoveControlNextPartMovePosition(MapSection mapSection, EnumIsExecute keepOrGo)
        {
            try
            {
                int sectionIndex = Vehicle.AseMovingGuide.GuideSectionIds.FindIndex(x => x == mapSection.Id);
                MapAddress address = Vehicle.Mapinfo.addressMap[Vehicle.AseMovingGuide.GuideAddressIds[sectionIndex + 1]];

                bool isEnd = false;
                EnumAddressDirection addressDirection = EnumAddressDirection.None;
                if (address.Id == Vehicle.AseMovingGuide.ToAddressId)
                {
                    addressDirection = address.TransferPortDirection;
                    isEnd = true;
                }
                int headAngle = (int)address.VehicleHeadAngle;
                int speed = (int)mapSection.Speed;

                if (Vehicle.MainFlowConfig.IsSimulation)
                {
                    Task.Run(() =>
                    {
                        SpinWait.SpinUntil(() => false, 1000);
                        AsePositionArgs positionArgs = new AsePositionArgs()
                        {
                            Arrival = isEnd ? EnumAseArrival.EndArrival : EnumAseArrival.Arrival,
                            MapPosition = address.Position
                        };
                        asePackage.ReceivePositionArgsQueue.Enqueue(positionArgs);
                    });
                }
                else
                {
                    if (isEnd)
                    {
                        var cmdId = GetCurTransferStep().CmdId;
                        if (Vehicle.AgvcTransCmdBuffer.ContainsKey(cmdId))
                        {
                            var transferCommand = Vehicle.AgvcTransCmdBuffer[cmdId];
                            switch (transferCommand.EnrouteState)
                            {
                                case CommandState.None:
                                    asePackage.PartMove(address.Position, headAngle, speed, EnumAseMoveCommandIsEnd.End, keepOrGo, EnumSlotSelect.None);
                                    break;
                                case CommandState.LoadEnroute:
                                case CommandState.UnloadEnroute:
                                    if (transferCommand.SlotNumber == EnumSlotNumber.L)
                                    {
                                        asePackage.PartMove(address.Position, headAngle, speed, EnumAseMoveCommandIsEnd.End, keepOrGo, EnumSlotSelect.Left);
                                    }
                                    else
                                    {
                                        asePackage.PartMove(address.Position, headAngle, speed, EnumAseMoveCommandIsEnd.End, keepOrGo, EnumSlotSelect.Right);
                                    }
                                    break;
                                default:
                                    asePackage.PartMove(address.Position, headAngle, speed, EnumAseMoveCommandIsEnd.End, keepOrGo, EnumSlotSelect.None);
                                    break;
                            }
                        }
                        else
                        {
                            SetAlarmFromAgvm(58);//200827 dabid+
                            //asePackage.PartMove(address.Position, headAngle, speed, EnumAseMoveCommandIsEnd.End, keepOrGo, EnumSlotSelect.None);//200827 dabid -
                        }
                    }
                    else
                    {
                        asePackage.PartMove(address.Position, headAngle, speed, EnumAseMoveCommandIsEnd.None, keepOrGo, EnumSlotSelect.None);
                    }
                }

                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"Send to MoveControl get reserve {mapSection.Id} ok , next end point [{address.Id}]({Convert.ToInt32(address.Position.X)},{Convert.ToInt32(address.Position.Y)}).");
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public bool IsMoveStep() => GetCurrentTransferStepType() == EnumTransferStepType.Move || GetCurrentTransferStepType() == EnumTransferStepType.MoveToCharger;

        public void AseMoveControl_OnMoveFinished(object sender, EnumMoveComplete status)
        {
            try
            {
                Vehicle.AseMoveStatus.IsMoveEnd = true;
                #region Not EnumMoveComplete.Success
                if (status == EnumMoveComplete.Fail)
                {
                    SetAlarmFromAgvm(6);
                    agvcConnector.ClearAllReserve();
                    Vehicle.AseMovingGuide = new AseMovingGuide();
                    if (IsAvoidMove)
                    {
                        agvcConnector.AvoidFail();
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[避車 移動 失敗] : Avoid Fail. ");
                        IsAvoidMove = false;
                    }
                    else if (IsOverrideMove)
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[變更路徑 移動失敗] :  Override Move Fail. ");
                        IsOverrideMove = false;
                    }
                    else
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[移動 失敗] : Move Fail. ");
                    }
                    // AsePackage_OnModeChangeEvent(this, EnumAutoState.Manual);
                    return;
                }

                // if (status == EnumMoveComplete.Pause)
                // {
                //     //VisitTransferStepsStatus = EnumThreadStatus.PauseComplete;
                //     LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[移動 暫停] : Move Pause.  checked ");
                //     agvcConnector.PauseAskReserve();
                //     PauseVisitTransferSteps();
                //     return;
                // }

                // if (status == EnumMoveComplete.Cancel)
                // {
                //     StopClearAndReset();
                //     if (IsAvoidMove)
                //     {
                //         agvcConnector.AvoidFail();
                //         LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[避車 移動 取消] : Avoid Move Cancel checked ");
                //         return;
                //     }
                //     else
                //     {
                //         LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[移動 取消] : Move Cancel checked ");
                //         return;
                //     }
                // }
                #endregion

                #region EnumMoveComplete.Success
                Vehicle.IsReAuto = false;

                agvcConnector.ClearAllReserve();

                if (IsAvoidMove)
                {
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[避車 二次定位 到站] : Avoid Move End Ok.");
                    Vehicle.AseMovingGuide = new AseMovingGuide();
                    Vehicle.AseMovingGuide.IsAvoidComplete = true;
                    agvcConnector.AvoidComplete();

                }
                else
                {
                    if (IsMoveStep())
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[移動 二次定位 到站] : Move End Ok.");

                        MoveCmdInfo moveCmdInfo = (MoveCmdInfo)GetCurTransferStep();
                        ArrivalStartCharge(moveCmdInfo.EndAddress);
                        Vehicle.AseMovingGuide = new AseMovingGuide();

                        if (IsNextTransferStepIdle())
                        {
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[移動 命令 完成] : Move Command End.");
                            TransferComplete(moveCmdInfo.CmdId);

                        }
                        else
                        {
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[移動完成 下一步] : VisitNextTransferStep.");

                            VisitNextTransferStep();
                        }
                    }
                    else
                    {
                        ArrivalStartCharge(Vehicle.AseMoveStatus.LastAddress);
                        VisitNextTransferStep();
                    }
                }

                IsAvoidMove = false;
                IsOverrideMove = false;

                #endregion
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public void TransferComplete(string cmdId)
        {
            try
            {
                WaitingTransferCompleteEnd = true;
                Vehicle.AseMoveStatus.IsMoveEnd = true;
                AgvcTransCmd agvcTransCmd = Vehicle.AgvcTransCmdBuffer[cmdId];
                agvcTransCmd.EnrouteState = CommandState.None;
                ClearTransferSteps(cmdId);
                ConcurrentDictionary<string, AgvcTransCmd> tempTransCmdBuffer = new ConcurrentDictionary<string, AgvcTransCmd>();
                foreach (var transCmd in Vehicle.AgvcTransCmdBuffer.Values.ToList())
                {
                    if (transCmd.CommandId != cmdId)
                    {
                        tempTransCmdBuffer.TryAdd(transCmd.CommandId, transCmd);
                    }
                }
                Vehicle.AgvcTransCmdBuffer = tempTransCmdBuffer;
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[命令 完成 回報] : ReportAgvcTransferComplete [{agvcTransCmd.AgvcTransCommandType}].");
                ReportAgvcTransferComplete(agvcTransCmd);
                OptimizeTransferStepsAfterTransferComplete();
                if (Vehicle.AgvcTransCmdBuffer.Count == 0)
                {
                    agvcConnector.NoCommand();
                }
                asePackage.SetTransferCommandInfoRequest(agvcTransCmd, EnumCommandInfoStep.End);
                GoNextTransferStep = true;
                WaitingTransferCompleteEnd = false;
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[命令 完成] : TransferComplete [{cmdId}].");
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void ArrivalStartCharge(MapAddress endAddress)
        {
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[到站 充電] : ArrivalStartCharge.");
                int chargeTimeSec = -1;
                if (GetNextTransferStepType() == EnumTransferStepType.Load || GetNextTransferStepType() == EnumTransferStepType.Unload)
                {
                    chargeTimeSec = Vehicle.MainFlowConfig.ChargeIntervalInRobotingSec;

                    if (Vehicle.AgvcTransCmdBuffer.Count == 2)
                    {
                        var curCmdId = GetCurTransferStep().CmdId.Trim();
                        var cmds = Vehicle.AgvcTransCmdBuffer.Values.ToArray();
                        var nextCmd = cmds[0].CommandId == curCmdId ? cmds[1] : cmds[0];
                        if (nextCmd.EnrouteState == CommandState.LoadEnroute)
                        {
                            if (nextCmd.LoadAddressId == endAddress.Id)
                            {
                                chargeTimeSec = 2 * Vehicle.MainFlowConfig.ChargeIntervalInRobotingSec;
                            }
                        }
                        else if (nextCmd.EnrouteState == CommandState.UnloadEnroute)
                        {
                            if (nextCmd.UnloadAddressId == endAddress.Id)
                            {
                                chargeTimeSec = 2 * Vehicle.MainFlowConfig.ChargeIntervalInRobotingSec;
                            }
                        }
                    }
                }


                Task.Run(() =>
               {
                   StartCharge(endAddress, chargeTimeSec);
               });
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private bool IsNextTransferStepIdle() => GetNextTransferStepType() == EnumTransferStepType.Empty;

        private void VisitNextTransferStep()
        {
            TransferStepsIndex++;
            GoNextTransferStep = true;
        }

        public TransferStep GetCurTransferStep()
        {
            TransferStep transferStep = new EmptyTransferStep();
            if (TransferStepsIndex < transferSteps.Count)
            {
                transferStep = transferSteps[TransferStepsIndex];
            }
            return transferStep;
        }

        private void ReportAgvcTransferComplete(AgvcTransCmd agvcTransCmd)
        {
            agvcConnector.TransferComplete(agvcTransCmd);
        }

        private void GetPioDirection(RobotCommand robotCommand)
        {
            try
            {
                MapAddress portAddress = Vehicle.Mapinfo.addressMap[robotCommand.PortAddressId];
                robotCommand.PioDirection = portAddress.PioDirection;
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #region Load Unload

        public void Load(LoadCmdInfo loadCmd)
        {
            try
            {
                GetPioDirection(loadCmd);

                AseCarrierSlotStatus aseCarrierSlotStatus = Vehicle.GetAseCarrierSlotStatus(loadCmd.SlotNumber);

                if (aseCarrierSlotStatus.CarrierSlotStatus != EnumAseCarrierSlotStatus.Empty)
                {
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨前 檢查 失敗] Pre Load Check Fail. Slot is not Empty.");

                    SetAlarmFromAgvm(000016);
                    return;
                }

                ReportLoadArrival(loadCmd);
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void ReportLoadArrival(LoadCmdInfo loadCmdInfo)
        {
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 到站] Load Arrival, [Port Adr = {loadCmdInfo.PortAddressId}][PioDirection = {loadCmdInfo.PioDirection}][Port Num = {loadCmdInfo.PortNumber}][GateType = {loadCmdInfo.GateType}]");
            agvcConnector.ReportLoadArrival(loadCmdInfo.CmdId);
        }

        private void AgvcConnector_OnAgvcAcceptLoadArrivalEvent(object sender, EventArgs e)
        {
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨到站 回報 成功] AgvcConnector_OnAgvcAcceptLoadArrivalEvent.");
            var loadCmd = (LoadCmdInfo)GetCurTransferStep();

            AseCarrierSlotStatus aseCarrierSlotStatus = Vehicle.GetAseCarrierSlotStatus(loadCmd.SlotNumber);

            try
            {
                agvcConnector.Loading(loadCmd.CmdId, loadCmd.SlotNumber);
                if (loadCmd.SlotNumber == EnumSlotNumber.L)
                {
                    Vehicle.LeftReadResult = BCRReadResult.BcrReadFail;
                }
                else
                {
                    Vehicle.RightReadResult = BCRReadResult.BcrReadFail;
                }

                if (Vehicle.MainFlowConfig.IsSimulation)
                {
                    SimulationLoad(loadCmd, aseCarrierSlotStatus);
                }
                else
                {
                    Task.Run(() => asePackage.DoRobotCommand(loadCmd));
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[執行 取貨] Loading, [Direction={loadCmd.PioDirection}][SlotNum={loadCmd.SlotNumber}][Load Adr={loadCmd.PortAddressId}][Load Port Num={loadCmd.PortNumber}]");
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void SimulationLoad(LoadCmdInfo loadCmd, AseCarrierSlotStatus aseCarrierSlotStatus)
        {
            SpinWait.SpinUntil(() => false, 3000);
            aseCarrierSlotStatus.CarrierId = loadCmd.CassetteId;
            aseCarrierSlotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.Loading;
            if (loadCmd.SlotNumber == EnumSlotNumber.L)
            {
                Vehicle.LeftReadResult = BCRReadResult.BcrNormal;
            }
            else
            {
                Vehicle.RightReadResult = BCRReadResult.BcrNormal;
            }
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[執行 取貨] Loading, [Direction={loadCmd.PioDirection}][SlotNum={loadCmd.SlotNumber}][Load Adr={loadCmd.PortAddressId}][Load Port Num={loadCmd.PortNumber}]");

            SpinWait.SpinUntil(() => false, 2000);
            AsePackage_OnRobotCommandFinishEvent(this, (RobotCommand)GetCurTransferStep());
        }

        public void Unload(UnloadCmdInfo unloadCmd)
        {
            //OptimizeTransferSteps();

            GetPioDirection(unloadCmd);

            AseCarrierSlotStatus aseCarrierSlotStatus = Vehicle.GetAseCarrierSlotStatus(unloadCmd.SlotNumber);

            if (aseCarrierSlotStatus.CarrierSlotStatus == EnumAseCarrierSlotStatus.Empty)
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放貨前 檢查 失敗] Pre Unload Check Fail. Slot is Empty.");

                SetAlarmFromAgvm(000017);
                return;
            }

            ReportUnloadArrival(unloadCmd);
        }

        private void AgvcConnector_OnAgvcAcceptUnloadArrivalEvent(object sender, EventArgs e)
        {
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放貨到站 回報 成功] AgvcConnector_OnAgvcAcceptUnloadArrivalEvent.");

                UnloadCmdInfo unloadCmd = (UnloadCmdInfo)GetCurTransferStep();
                AseCarrierSlotStatus aseCarrierSlotStatus = Vehicle.GetAseCarrierSlotStatus(unloadCmd.SlotNumber);

                agvcConnector.Unloading(unloadCmd.CmdId, unloadCmd.SlotNumber);

                if (Vehicle.MainFlowConfig.IsSimulation)
                {
                    SimulationUnload(unloadCmd, aseCarrierSlotStatus);
                }
                else
                {
                    Task.Run(() => asePackage.DoRobotCommand(unloadCmd));
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[執行 放貨] : Unloading, [Direction{unloadCmd.PioDirection}][SlotNum={unloadCmd.SlotNumber}][Unload Adr={unloadCmd.PortAddressId}][Unload Port Num={unloadCmd.PortNumber}]");
                }

            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }

        }

        private void ReportUnloadArrival(UnloadCmdInfo unloadCmd)
        {
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放貨 到站] : Unload Arrival, [Port Adr = {unloadCmd.PortAddressId}][PioDirection = {unloadCmd.PioDirection}][PortNumber = {unloadCmd.PortNumber}]GateType = {unloadCmd.GateType}]");
            agvcConnector.ReportUnloadArrival(unloadCmd.CmdId);
        }

        private void SimulationUnload(UnloadCmdInfo unloadCmd, AseCarrierSlotStatus aseCarrierSlotStatus)
        {
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[執行 放貨] : Unloading, [Direction{unloadCmd.PioDirection}][SlotNum={unloadCmd.SlotNumber}][Unload Adr={unloadCmd.PortAddressId}][Unload Port Num={unloadCmd.PortNumber}]");
            SpinWait.SpinUntil(() => false, 3000);
            aseCarrierSlotStatus.CarrierId = "";
            aseCarrierSlotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.Empty;
            SpinWait.SpinUntil(() => false, 2000);
            AsePackage_OnRobotCommandFinishEvent(this, (RobotCommand)GetCurTransferStep());
        }

        private void AsePackage_OnRobotCommandErrorEvent(object sender, RobotCommand robotCommand)
        {
            if (IsStopChargTimeoutInRobotStep)
            {
                IsStopChargTimeoutInRobotStep = false;
                SetAlarmFromAgvm(14);
            }
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "[手臂 命令 失敗] AseRobotControl_OnRobotCommandErrorEvent");
            // AsePackage_OnModeChangeEvent(this, EnumAutoState.Manual);
        }

        public void AsePackage_OnRobotCommandFinishEvent(object sender, RobotCommand robotCommand)
        {
            try
            {
                if (IsStopChargTimeoutInRobotStep)
                {
                    IsStopChargTimeoutInRobotStep = false;
                    SetAlarmFromAgvm(14);
                }
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "[手臂 命令 完成] AseRobotContorl_OnRobotCommandFinishEvent");
                EnumTransferStepType transferStepType = robotCommand.GetTransferStepType();
                if (transferStepType == EnumTransferStepType.Load)
                {
                    ConfirmBcrReadResultInLoad(robotCommand);
                    ReportAgvcLoadComplete(robotCommand.CmdId);
                }
                else if (transferStepType == EnumTransferStepType.Unload)
                {
                    ConfirmBcrReadResultInUnload(robotCommand);
                }
                else
                {
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[{transferStepType}] AsePackage_OnRobotCommandFinishEvent?");
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void AgvcConnector_OnAgvcAcceptLoadCompleteEvent(object sender, EventArgs e)
        {
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "[取貨完成回報 成功] AgvcConnector_OnAgvcAcceptLoadCompleteEvent.");

                ReportAgvcBcrRead();
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void ConfirmBcrReadResultInLoad(RobotCommand robotCommand)
        {
            try
            {
                EnumSlotNumber slotNumber = robotCommand.SlotNumber;
                AseCarrierSlotStatus slotStatus = slotNumber == EnumSlotNumber.L ? Vehicle.AseCarrierSlotL : Vehicle.AseCarrierSlotR;

                if (!Vehicle.MainFlowConfig.BcrByPass)
                {
                    switch (slotStatus.CarrierSlotStatus)
                    {
                        case EnumAseCarrierSlotStatus.Empty:
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 讀取 失敗] CST ID is empty.");

                                slotStatus.CarrierId = "";
                                slotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.Empty;
                                if (slotNumber == EnumSlotNumber.L)
                                {
                                    Vehicle.AseCarrierSlotL = slotStatus;
                                    Vehicle.LeftReadResult = BCRReadResult.BcrReadFail;
                                }
                                else
                                {
                                    Vehicle.AseCarrierSlotR = slotStatus;
                                    Vehicle.RightReadResult = BCRReadResult.BcrReadFail;
                                }

                                SetAlarmFromAgvm(000051);
                            }
                            break;
                        case EnumAseCarrierSlotStatus.Loading:
                            if (robotCommand.CassetteId.Trim() == slotStatus.CarrierId.Trim())
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 讀取 成功] CST ID = [{slotStatus.CarrierId.Trim()}] read ok.");

                                if (slotNumber == EnumSlotNumber.L)
                                {
                                    Vehicle.LeftReadResult = BCRReadResult.BcrNormal;
                                }
                                else
                                {
                                    Vehicle.RightReadResult = BCRReadResult.BcrNormal;
                                }
                            }
                            else
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 讀取 失敗]Read CST ID = [{slotStatus.CarrierId}], unmatched command CST ID = [{robotCommand.CassetteId}]");

                                if (slotNumber == EnumSlotNumber.L)
                                {
                                    Vehicle.LeftReadResult = BCRReadResult.BcrMisMatch;
                                }
                                else
                                {
                                    Vehicle.RightReadResult = BCRReadResult.BcrMisMatch;
                                }

                                SetAlarmFromAgvm(000028);
                            }
                            break;
                        case EnumAseCarrierSlotStatus.ReadFail:
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "[取貨 讀取 失敗] ReadFail.");

                                slotStatus.CarrierId = "ReadFail";
                                slotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.ReadFail;

                                if (slotNumber == EnumSlotNumber.L)
                                {
                                    Vehicle.AseCarrierSlotL = slotStatus;
                                    Vehicle.LeftReadResult = BCRReadResult.BcrReadFail;
                                }
                                else
                                {
                                    Vehicle.AseCarrierSlotR = slotStatus;
                                    Vehicle.RightReadResult = BCRReadResult.BcrReadFail;
                                }
                                SetAlarmFromAgvm(000004);
                            }
                            break;
                        case EnumAseCarrierSlotStatus.PositionError:
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 讀取 凸片] CST Position Error.");

                                slotStatus.CarrierId = "PositionError";
                                slotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.PositionError;

                                if (slotNumber == EnumSlotNumber.L)
                                {
                                    Vehicle.AseCarrierSlotL = slotStatus;
                                    Vehicle.LeftReadResult = BCRReadResult.BcrReadFail;
                                }
                                else
                                {
                                    Vehicle.AseCarrierSlotR = slotStatus;
                                    Vehicle.RightReadResult = BCRReadResult.BcrReadFail;
                                }

                                SetAlarmFromAgvm(000051);
                                return;
                            }
                        default:
                            break;
                    }
                }
                else
                {
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 讀取 關閉] BcrByPass.");

                    switch (slotStatus.CarrierSlotStatus)
                    {
                        case EnumAseCarrierSlotStatus.Empty:
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 讀取 失敗] BcrByPass, slot is empty.");

                                slotStatus.CarrierId = "";
                                slotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.Empty;
                                if (slotNumber == EnumSlotNumber.L)
                                {
                                    Vehicle.AseCarrierSlotL = slotStatus;
                                    Vehicle.LeftReadResult = BCRReadResult.BcrReadFail;
                                }
                                else
                                {
                                    Vehicle.AseCarrierSlotR = slotStatus;
                                    Vehicle.RightReadResult = BCRReadResult.BcrReadFail;

                                }
                            }
                            break;
                        case EnumAseCarrierSlotStatus.ReadFail:
                        case EnumAseCarrierSlotStatus.Loading:
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 讀取 成功] BcrByPass, loading is true.");
                                slotStatus.CarrierId = robotCommand.CassetteId.Trim();
                                slotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.Loading;

                                if (slotNumber == EnumSlotNumber.L)
                                {
                                    Vehicle.AseCarrierSlotL = slotStatus;
                                    Vehicle.LeftReadResult = BCRReadResult.BcrNormal;
                                }
                                else
                                {
                                    Vehicle.AseCarrierSlotR = slotStatus;
                                    Vehicle.RightReadResult = BCRReadResult.BcrNormal;
                                }
                            }
                            break;
                        case EnumAseCarrierSlotStatus.PositionError:
                            {
                                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[取貨 讀取 凸片] CST Position Error.");

                                slotStatus.CarrierId = "PositionError";
                                slotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.PositionError;

                                if (slotNumber == EnumSlotNumber.L)
                                {
                                    Vehicle.AseCarrierSlotL = slotStatus;
                                    Vehicle.LeftReadResult = BCRReadResult.BcrReadFail;
                                }
                                else
                                {
                                    Vehicle.AseCarrierSlotR = slotStatus;
                                    Vehicle.RightReadResult = BCRReadResult.BcrReadFail;
                                }

                                SetAlarmFromAgvm(000051);
                                return;
                            }
                        default:
                            break;
                    }
                }

            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }
        private void ConfirmBcrReadResultInUnload(RobotCommand robotCommand)
        {
            var slotNumber = robotCommand.SlotNumber;
            AseCarrierSlotStatus aseCarrierSlotStatus = Vehicle.GetAseCarrierSlotStatus(slotNumber);
            switch (aseCarrierSlotStatus.CarrierSlotStatus)
            {
                case EnumAseCarrierSlotStatus.Empty:
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放貨 完成] : Unload Complete");
                    ReportAgvcUnloadComplete(robotCommand.CmdId);
                    break;
                case EnumAseCarrierSlotStatus.Loading:
                case EnumAseCarrierSlotStatus.ReadFail:
                    if (Vehicle.AgvcTransCmdBuffer.ContainsKey(robotCommand.CmdId))
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放貨 失敗] : CarrierSlotStatus = [{aseCarrierSlotStatus.CarrierSlotStatus}].");
                        Vehicle.AgvcTransCmdBuffer[robotCommand.CmdId].CompleteStatus = CompleteStatus.VehicleAbort;
                        TransferComplete(robotCommand.CmdId);
                    }
                    else
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放貨 失敗] AseRobotContorl_OnRobotCommandFinishEvent, can not find command [ID = {robotCommand.CmdId}]. Reset all.");
                        StopClearAndReset();
                    }
                    break;
                case EnumAseCarrierSlotStatus.PositionError:
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放貨 失敗 凸片] : PositionError.");
                    SetAlarmFromAgvm(51);
                    break;
            }

        }

        private void ReportAgvcUnloadComplete(string cmdId)
        {
            try
            {
                var robotCmdInfo = (RobotCommand)GetCurTransferStep();
                var slotNumber = robotCmdInfo.SlotNumber;
                AseCarrierSlotStatus aseCarrierSlotStatus = slotNumber == EnumSlotNumber.L ? Vehicle.AseCarrierSlotL : Vehicle.AseCarrierSlotR;

                agvcConnector.UnloadComplete(cmdId);
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }
        private void ReportAgvcLoadComplete(string cmdId)
        {
            agvcConnector.LoadComplete(cmdId);
        }
        private void ReportAgvcBcrRead()
        {
            agvcConnector.SendRecv_Cmd136_CstIdReadReport();
        }

        private void AsePackage_OnRobotInterlockErrorEvent(object sender, RobotCommand robotCommand)
        {
            try
            {
                if (IsStopChargTimeoutInRobotStep)
                {
                    IsStopChargTimeoutInRobotStep = false;
                    SetAlarmFromAgvm(14);
                }
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "[手臂 交握 失敗] AseRobotControl_OnRobotInterlockErrorEvent");
                ResetAllAlarmsFromAgvm();
                var curCmdId = robotCommand.CmdId;
                if (Vehicle.AgvcTransCmdBuffer.ContainsKey(curCmdId))
                {
                    Vehicle.AgvcTransCmdBuffer[curCmdId].CompleteStatus = CompleteStatus.InterlockError;
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "[手臂 交握 失敗] TransferComplete");
                    TransferComplete(curCmdId);
                }
                else
                {
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[手臂 交握 失敗] AseRobotControl_OnRobotInterlockErrorEvent, can not find command [ID = {curCmdId}]. Reset all.");
                    StopClearAndReset();
                }
            }
            catch (Exception ex)
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[手臂 交握 失敗] InterlockError Exception.");
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void AgvcConnector_OnSendRecvTimeoutEvent(object sender, EventArgs e)
        {
            SetAlarmFromAgvm(38);
            // AsePackage_OnModeChangeEvent(this, EnumAutoState.Manual);
        }

        private void AgvcConnector_OnAgvcContinueBcrReadEvent(object sender, EventArgs e)
        {
            OptimizeTransferStepsAfterLoadEnd();
        }

        private void AgvcConnector_OnAgvcAcceptMoveArrivalEvent(object sender, EventArgs e)
        {
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, "MoveArrival");
            if (transferSteps.Count > 0)
            {
                VisitNextTransferStep();
            }
        }

        private void AgvcConnector_OnAgvcAcceptUnloadCompleteEvent(object sender, EventArgs e)
        {
            try
            {
                var transferStep = GetCurTransferStep();
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[命令完成 {transferStep.GetTransferStepType()}] TransferComplete.");
                TransferComplete(transferStep.CmdId);
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public void OptimizeTransferStepsAfterLoadEnd()
        {
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"Optimize Transfer Steps");

                Vehicle.IsOptimize = true;
                IsVisitTransferStepPause = true;

                var curCmdId = GetCurTransferStep().CmdId;
                var curCmd = Vehicle.AgvcTransCmdBuffer[curCmdId];
                var curStep = GetCurTransferStep();
                transferSteps = new List<TransferStep>();
                TransferStepsIndex = 1;
                transferSteps.Add(curStep);

                if (!Vehicle.MainFlowConfig.DualCommandOptimize)
                {
                    if (curCmd.AgvcTransCommandType == EnumAgvcTransCommandType.LoadUnload)
                    {
                        curCmd.EnrouteState = CommandState.UnloadEnroute;
                        TransferStepsAddMoveCmdInfo(curCmd.UnloadAddressId, curCmdId);
                        TransferStepsAddUnloadCmdInfo(curCmd);
                    }
                    else
                    {
                        transferSteps = new List<TransferStep>();
                        TransferStepsIndex = 0;
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[命令完成 Load] TransferComplete.");
                        TransferComplete(curCmdId);
                    }
                }
                else
                {
                    if (Vehicle.AgvcTransCmdBuffer.Count <= 1)
                    {
                        if (curCmd.AgvcTransCommandType == EnumAgvcTransCommandType.LoadUnload)
                        {
                            curCmd.EnrouteState = CommandState.UnloadEnroute;
                            TransferStepsAddMoveCmdInfo(curCmd.UnloadAddressId, curCmdId);
                            TransferStepsAddUnloadCmdInfo(curCmd);
                        }
                        else
                        {
                            transferSteps = new List<TransferStep>();
                            TransferStepsIndex = 0;
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[命令完成 Load] TransferComplete.");
                            TransferComplete(curCmdId);
                        }
                    }
                    else
                    {
                        if (curCmd.GetCommandActionType() == CommandActionType.Load)
                        {
                            transferSteps = new List<TransferStep>();
                            TransferStepsIndex = 0;
                            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[命令完成 Load] TransferComplete.");
                            TransferComplete(curCmdId);
                        }
                        else
                        {
                            curCmd.EnrouteState = CommandState.UnloadEnroute;
                            var disCurCmdUnload = DistanceFromLastPosition(curCmd.UnloadAddressId);

                            var transferCommands = Vehicle.AgvcTransCmdBuffer.Values.ToList();
                            int nextCmdIndex = transferCommands[0].CommandId == curCmdId ? 1 : 0;
                            var nextCmd = transferCommands[nextCmdIndex];
                            var nextCmdId = nextCmd.CommandId;

                            if (nextCmd.EnrouteState == CommandState.LoadEnroute)
                            {
                                var disNextCmdLoad = DistanceFromLastPosition(nextCmd.LoadAddressId);
                                if (disCurCmdUnload <= disNextCmdLoad)
                                {
                                    TransferStepsAddMoveCmdInfo(curCmd.UnloadAddressId, curCmdId);
                                    TransferStepsAddUnloadCmdInfo(curCmd);
                                }
                                else
                                {
                                    TransferStepsAddMoveCmdInfo(nextCmd.LoadAddressId, nextCmdId);
                                    TransferStepsAddLoadCmdInfo(nextCmd);
                                }
                            }
                            else if (nextCmd.EnrouteState == CommandState.UnloadEnroute)
                            {
                                var disNextCmdUnload = DistanceFromLastPosition(nextCmd.UnloadAddressId);
                                if (disCurCmdUnload <= disNextCmdUnload)
                                {
                                    TransferStepsAddMoveCmdInfo(curCmd.UnloadAddressId, curCmdId);
                                    TransferStepsAddUnloadCmdInfo(curCmd);
                                }
                                else
                                {
                                    TransferStepsAddMoveCmdInfo(nextCmd.UnloadAddressId, nextCmdId);
                                    TransferStepsAddUnloadCmdInfo(nextCmd);
                                }
                            }
                        }
                    }
                }

                Vehicle.IsOptimize = false;

                GoNextTransferStep = true;
                IsVisitTransferStepPause = false;
            }
            catch (Exception ex)
            {
                Vehicle.IsOptimize = false;
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private int DistanceFromLastPosition(string addressId)
        {
            var lastPosition = Vehicle.AseMoveStatus.LastMapPosition;
            var addressPosition = Vehicle.Mapinfo.addressMap[addressId].Position;
            return (int)mapHandler.GetDistance(lastPosition, addressPosition);
        }

        private void OptimizeTransferStepsAfterTransferComplete()
        {
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[選擇 下一筆執行命令] OptimizeTransferStepsAfterTransferComplete");
                Vehicle.IsOptimize = true;
                transferSteps = new List<TransferStep>();
                TransferStepsIndex = 0;

                if (Vehicle.AgvcTransCmdBuffer.Count > 1)
                {
                    if (!Vehicle.MainFlowConfig.DualCommandOptimize)
                    {
                        List<AgvcTransCmd> agvcTransCmds = Vehicle.AgvcTransCmdBuffer.Values.ToList();
                        AgvcTransCmd onlyCmd = agvcTransCmds[0];

                        switch (onlyCmd.EnrouteState)
                        {
                            case CommandState.None:
                                TransferStepsAddMoveCmdInfo(onlyCmd.UnloadAddressId, onlyCmd.CommandId);
                                transferSteps.Add(new EmptyTransferStep());
                                break;
                            case CommandState.LoadEnroute:
                                TransferStepsAddMoveCmdInfo(onlyCmd.LoadAddressId, onlyCmd.CommandId);
                                TransferStepsAddLoadCmdInfo(onlyCmd);
                                break;
                            case CommandState.UnloadEnroute:
                                TransferStepsAddMoveCmdInfo(onlyCmd.UnloadAddressId, onlyCmd.CommandId);
                                TransferStepsAddUnloadCmdInfo(onlyCmd);
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        List<AgvcTransCmd> agvcTransCmds = Vehicle.AgvcTransCmdBuffer.Values.ToList();
                        AgvcTransCmd cmd001 = agvcTransCmds[0];

                        switch (cmd001.EnrouteState)
                        {
                            case CommandState.None:
                                {
                                    TransferStepsAddMoveCmdInfo(cmd001.UnloadAddressId, cmd001.CommandId);
                                    transferSteps.Add(new EmptyTransferStep());
                                }
                                break;
                            case CommandState.LoadEnroute:
                                {
                                    var disCmd001Load = DistanceFromLastPosition(cmd001.LoadAddressId);

                                    AgvcTransCmd cmd002 = agvcTransCmds[1];
                                    if (cmd002.EnrouteState == CommandState.LoadEnroute)
                                    {
                                        var disCmd002Load = DistanceFromLastPosition(cmd002.LoadAddressId);
                                        if (disCmd001Load <= disCmd002Load)
                                        {
                                            TransferStepsAddMoveCmdInfo(cmd001.LoadAddressId, cmd001.CommandId);
                                            TransferStepsAddLoadCmdInfo(cmd001);
                                        }
                                        else
                                        {
                                            TransferStepsAddMoveCmdInfo(cmd002.LoadAddressId, cmd002.CommandId);
                                            TransferStepsAddLoadCmdInfo(cmd002);
                                        }
                                    }
                                    else if (cmd002.EnrouteState == CommandState.UnloadEnroute)
                                    {
                                        var disCmd002Unload = DistanceFromLastPosition(cmd002.UnloadAddressId);
                                        if (disCmd001Load <= disCmd002Unload)
                                        {
                                            TransferStepsAddMoveCmdInfo(cmd001.LoadAddressId, cmd001.CommandId);
                                            TransferStepsAddLoadCmdInfo(cmd001);
                                        }
                                        else
                                        {
                                            TransferStepsAddMoveCmdInfo(cmd002.UnloadAddressId, cmd002.CommandId);
                                            TransferStepsAddUnloadCmdInfo(cmd002);
                                        }
                                    }
                                    else
                                    {
                                        StopClearAndReset();
                                        SetAlarmFromAgvm(1);
                                    }
                                }
                                break;
                            case CommandState.UnloadEnroute:
                                {
                                    var disCmd001Unload = DistanceFromLastPosition(cmd001.UnloadAddressId);

                                    AgvcTransCmd cmd002 = agvcTransCmds[1];
                                    if (cmd002.EnrouteState == CommandState.LoadEnroute)
                                    {
                                        var disCmd002Load = DistanceFromLastPosition(cmd002.LoadAddressId);
                                        if (disCmd001Unload <= disCmd002Load)
                                        {
                                            TransferStepsAddMoveCmdInfo(cmd001.UnloadAddressId, cmd001.CommandId);
                                            TransferStepsAddUnloadCmdInfo(cmd001);
                                        }
                                        else
                                        {
                                            TransferStepsAddMoveCmdInfo(cmd002.LoadAddressId, cmd002.CommandId);
                                            TransferStepsAddLoadCmdInfo(cmd002);
                                        }
                                    }
                                    else if (cmd002.EnrouteState == CommandState.UnloadEnroute)
                                    {
                                        var disCmd002Unload = DistanceFromLastPosition(cmd002.UnloadAddressId);
                                        if (disCmd001Unload <= disCmd002Unload)
                                        {
                                            TransferStepsAddMoveCmdInfo(cmd001.UnloadAddressId, cmd001.CommandId);
                                            TransferStepsAddUnloadCmdInfo(cmd001);
                                        }
                                        else
                                        {
                                            TransferStepsAddMoveCmdInfo(cmd002.UnloadAddressId, cmd002.CommandId);
                                            TransferStepsAddUnloadCmdInfo(cmd002);
                                        }
                                    }
                                    else
                                    {
                                        StopClearAndReset();
                                        SetAlarmFromAgvm(1);
                                    }
                                }
                                break;
                            default:
                                break;
                        }

                    }
                }
                else if (Vehicle.AgvcTransCmdBuffer.Count == 1)
                {
                    List<AgvcTransCmd> agvcTransCmds = Vehicle.AgvcTransCmdBuffer.Values.ToList();
                    AgvcTransCmd onlyCmd = agvcTransCmds[0];

                    switch (onlyCmd.EnrouteState)
                    {
                        case CommandState.None:
                            TransferStepsAddMoveCmdInfo(onlyCmd.UnloadAddressId, onlyCmd.CommandId);
                            transferSteps.Add(new EmptyTransferStep());
                            break;
                        case CommandState.LoadEnroute:
                            TransferStepsAddMoveCmdInfo(onlyCmd.LoadAddressId, onlyCmd.CommandId);
                            TransferStepsAddLoadCmdInfo(onlyCmd);
                            break;
                        case CommandState.UnloadEnroute:
                            TransferStepsAddMoveCmdInfo(onlyCmd.UnloadAddressId, onlyCmd.CommandId);
                            TransferStepsAddUnloadCmdInfo(onlyCmd);
                            break;
                        default:
                            break;
                    }
                }

                Vehicle.IsOptimize = false;

            }
            catch (Exception ex)
            {
                Vehicle.IsOptimize = false;
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void AgvcConnector_OnCstRenameEvent(object sender, EnumSlotNumber slotNumber)
        {
            try
            {
                AseCarrierSlotStatus slotStatus = slotNumber == EnumSlotNumber.L ? Vehicle.AseCarrierSlotL : Vehicle.AseCarrierSlotR;
                asePackage.CstRename(slotStatus);
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #endregion        

        public void SetupAseMovingGuideMovingSections()
        {
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[設定 路線] Setup MovingGuide.");

                AseMovingGuide aseMovingGuide = new AseMovingGuide(Vehicle.AseMovingGuide);
                aseMovingGuide.MovingSections.Clear();
                for (int i = 0; i < Vehicle.AseMovingGuide.GuideSectionIds.Count; i++)
                {
                    MapSection mapSection = new MapSection();
                    string sectionId = aseMovingGuide.GuideSectionIds[i].Trim();
                    string addressId = aseMovingGuide.GuideAddressIds[i + 1].Trim();
                    if (!Vehicle.Mapinfo.sectionMap.ContainsKey(sectionId))
                    {
                        throw new Exception($"Map info has no this section ID.[{sectionId}]");
                    }
                    else if (!Vehicle.Mapinfo.addressMap.ContainsKey(addressId))
                    {
                        throw new Exception($"Map info has no this address ID.[{addressId}]");
                    }

                    mapSection = Vehicle.Mapinfo.sectionMap[sectionId];
                    mapSection.CmdDirection = addressId == mapSection.TailAddress.Id ? EnumCommandDirection.Forward : EnumCommandDirection.Backward;
                    aseMovingGuide.MovingSections.Add(mapSection);
                }
                Vehicle.AseMovingGuide = aseMovingGuide;

                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[設定 路線 成功] Setup MovingGuide OK.");
            }
            catch (Exception ex)
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[設定 路線 失敗] Setup MovingGuide Fail.");
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                Vehicle.AseMovingGuide.MovingSections = new List<MapSection>();
                SetAlarmFromAgvm(18);
                StopClearAndReset();
            }
        }
        private void UpdateMovePassSections(string id)
        {
            int getReserveOkSectionIndex = 0;
            try
            {
                var getReserveOkSections = agvcConnector.GetReserveOkSections();
                getReserveOkSectionIndex = getReserveOkSections.FindIndex(x => x.Id == id);
                if (getReserveOkSectionIndex < 0) return;
                for (int i = 0; i < getReserveOkSectionIndex; i++)
                {
                    //Remove passed section in ReserveOkSection
                    agvcConnector.DequeueGotReserveOkSections();
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"FAIL [SecId={id}][Index={getReserveOkSectionIndex}]");
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }

        }

        private void AgvcConnector_OnStopClearAndResetEvent(object sender, EventArgs e)
        {
            StopClearAndReset();
        }

        public void StopClearAndReset()
        {
            try
            {
                var isPause = Vehicle.PauseFlags[PauseType.Normal];
                PauseTransfer();
                agvcConnector.ClearAllReserve();
                Vehicle.AseMovingGuide = new AseMovingGuide();
                StopVehicle();

                var transferCommands = Vehicle.AgvcTransCmdBuffer.Values.ToList();
                foreach (var transCmd in transferCommands)
                {
                    Vehicle.AgvcTransCmdBuffer[transCmd.CommandId].CompleteStatus = GetStopAndClearCompleteStatus(transCmd.CompleteStatus);
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[結束 命令] TransferComplete [{Vehicle.AgvcTransCmdBuffer[transCmd.CommandId].CompleteStatus}].");
                    TransferComplete(transCmd.CommandId);
                }

                if (Vehicle.AseCarrierSlotL.CarrierSlotStatus == EnumAseCarrierSlotStatus.Loading || Vehicle.AseCarrierSlotR.CarrierSlotStatus == EnumAseCarrierSlotStatus.Loading)
                {
                    asePackage.ReadCarrierId();
                }

                //if (Vehicle.AseMovingGuide.PauseStatus == VhStopSingle.On)
                //{
                //    Vehicle.AseMovingGuide.PauseStatus = VhStopSingle.Off;
                //    agvcConnector.StatusChangeReport();
                //}

                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[停止 與 重置] StopClearAndReset.");
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private CompleteStatus GetStopAndClearCompleteStatus(CompleteStatus completeStatus)
        {
            switch (completeStatus)
            {
                case CompleteStatus.Move:
                case CompleteStatus.Load:
                case CompleteStatus.Unload:
                case CompleteStatus.Loadunload:
                case CompleteStatus.MoveToCharger:
                    return CompleteStatus.VehicleAbort;
                case CompleteStatus.Cancel:
                case CompleteStatus.Abort:
                case CompleteStatus.VehicleAbort:
                case CompleteStatus.IdmisMatch:
                case CompleteStatus.IdreadFailed:
                case CompleteStatus.InterlockError:
                case CompleteStatus.LongTimeInaction:
                case CompleteStatus.ForceFinishByOp:
                default:
                    return completeStatus;
            }
        }

        public EnumTransferStepType GetCurrentTransferStepType()
        {
            try
            {
                if (transferSteps.Count > 0)
                {
                    if (TransferStepsIndex < transferSteps.Count)
                    {
                        return transferSteps[TransferStepsIndex].GetTransferStepType();
                    }
                }

                return EnumTransferStepType.Empty;
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                return EnumTransferStepType.Empty;
            }
        }
        public EnumTransferStepType GetNextTransferStepType()
        {
            try
            {
                if (transferSteps.Count > 0)
                {
                    if (TransferStepsIndex + 1 < transferSteps.Count)
                    {
                        return transferSteps[TransferStepsIndex + 1].GetTransferStepType();
                    }
                }

                return EnumTransferStepType.Empty;
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
                return EnumTransferStepType.Empty;
            }
        }
        public int GetTransferStepsCount()
        {
            return transferSteps.Count;
        }
        public void StopVehicle()
        {
            asePackage.MoveStop();
            asePackage.ClearRobotCommand();
            asePackage.StopCharge();

            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"MainFlow : Stop Vehicle, [MoveState={Vehicle.AseMoveStatus.AseMoveState}][IsCharging={Vehicle.IsCharging}]");
        }

        public void SetupVehicleSoc(int percentage)
        {
            asePackage.SetPercentage(percentage);
        }

        private void AgvcConnector_OnRenameCassetteIdEvent(object sender, AseCarrierSlotStatus e)
        {
            try
            {
                AgvcTransCmd agvcTransCmd = Vehicle.AgvcTransCmdBuffer.Values.First(x => x.SlotNumber == e.SlotNumber);
                agvcTransCmd.CassetteId = e.CarrierId;
                Vehicle.AgvcTransCmdBuffer[agvcTransCmd.CommandId] = agvcTransCmd;

                if (transferSteps.Count > 0)
                {
                    foreach (var transferStep in transferSteps)
                    {
                        if (transferStep.CmdId == agvcTransCmd.CommandId && IsRobCommand(transferStep))
                        {
                            ((RobotCommand)transferStep).CassetteId = e.CarrierId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private bool IsRobCommand(TransferStep transferStep) => transferStep.GetTransferStepType() == EnumTransferStepType.Load || transferStep.GetTransferStepType() == EnumTransferStepType.Unload;

        public void AgvcConnector_OnCmdPauseEvent(ushort iSeqNum, PauseEvent pauseEvent, PauseType pauseType)
        {
            try
            {
                Vehicle.PauseFlags[pauseType] = true;
                PauseTransfer();
                asePackage.MovePause();
                agvcConnector.PauseReply(iSeqNum, 0, PauseEvent.Pause);
                agvcConnector.StatusChangeReport();
                //if (Vehicle.AseMovingGuide.PauseStatus == VhStopSingle.Off)
                //{
                //    Vehicle.AseMovingGuide.PauseStatus = VhStopSingle.On;

                //}

                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[執行 暫停] [{pauseEvent}][{pauseType}]");
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public void AgvcConnector_OnCmdResumeEvent(ushort iSeqNum, PauseEvent pauseEvent, PauseType pauseType)
        {
            try
            {
                if (pauseType == PauseType.All)
                {
                    Vehicle.PauseFlags = new ConcurrentDictionary<PauseType, bool>(Enum.GetValues(typeof(PauseType)).Cast<PauseType>().ToDictionary(x => x, x => false));
                    ResumeMiddler(iSeqNum, pauseEvent, pauseType);
                }
                else
                {
                    Vehicle.PauseFlags[pauseType] = false;
                    agvcConnector.PauseReply(iSeqNum, 0, PauseEvent.Continue);

                    if (!Vehicle.IsPause())
                    {
                        ResumeMiddler(iSeqNum, pauseEvent, pauseType);
                    }
                    else
                    {
                        LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[尚有 其他 暫停旗標] [{pauseEvent}][{pauseType}]");
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void ResumeMiddler(ushort iSeqNum, PauseEvent pauseEvent, PauseType pauseType)
        {
            agvcConnector.PauseReply(iSeqNum, 0, PauseEvent.Continue);
            asePackage.MoveContinue();
            ResumeTransfer();
            agvcConnector.StatusChangeReport();

            //if (Vehicle.AseMovingGuide.PauseStatus == VhStopSingle.On)
            //{
            //    Vehicle.AseMovingGuide.PauseStatus = VhStopSingle.Off;
            //    agvcConnector.StatusChangeReport();
            //}

            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[執行 續行] [{pauseEvent}][{pauseType}]");
        }

        public void AgvcConnector_OnCmdCancelAbortEvent(ushort iSeqNum, ID_37_TRANS_CANCEL_REQUEST receive)
        {
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"MainFlow : Get [{receive.CancelAction}] Command.");
                PauseTransfer();
                agvcConnector.CancelAbortReply(iSeqNum, 0, receive);

                string abortCmdId = receive.CmdID.Trim();
                var step = GetCurTransferStep();
                bool IsAbortCurCommand = GetCurTransferStep().CmdId == abortCmdId;
                var targetAbortCmd = Vehicle.AgvcTransCmdBuffer[abortCmdId];

                if (IsAbortCurCommand)
                {
                    agvcConnector.ClearAllReserve();
                    asePackage.MoveStop();
                    targetAbortCmd.CompleteStatus = GetCompleteStatusFromCancelRequest(receive.CancelAction);
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放棄 當前 命令] TransferComplete [{targetAbortCmd.CompleteStatus}].");
                    TransferComplete(targetAbortCmd.CommandId);
                }
                else
                {

                    targetAbortCmd.EnrouteState = CommandState.None;
                    targetAbortCmd.CompleteStatus = GetCompleteStatusFromCancelRequest(receive.CancelAction);

                    ConcurrentDictionary<string, AgvcTransCmd> tempTransCmdBuffer = new ConcurrentDictionary<string, AgvcTransCmd>();
                    foreach (var transCmd in Vehicle.AgvcTransCmdBuffer.Values.ToList())
                    {
                        if (transCmd.CommandId != abortCmdId)
                        {
                            tempTransCmdBuffer.TryAdd(transCmd.CommandId, transCmd);
                        }
                    }
                    Vehicle.AgvcTransCmdBuffer = tempTransCmdBuffer;
                    agvcConnector.StatusChangeReport();
                    LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"[放棄 背景 命令] TransferComplete [{targetAbortCmd.CompleteStatus}].");
                    ReportAgvcTransferComplete(targetAbortCmd);

                    asePackage.SetTransferCommandInfoRequest(targetAbortCmd, EnumCommandInfoStep.End);

                    if (Vehicle.AgvcTransCmdBuffer.Count == 0)
                    {
                        agvcConnector.NoCommand();
                        GoNextTransferStep = true;
                        IsVisitTransferStepPause = false;
                    }
                }

                ResumeTransfer();
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private CompleteStatus GetCompleteStatusFromCancelRequest(CancelActionType cancelAction)
        {
            switch (cancelAction)
            {
                case CancelActionType.CmdCancel:
                    return CompleteStatus.Cancel;
                case CancelActionType.CmdCancelIdMismatch:
                    return CompleteStatus.IdmisMatch;
                case CancelActionType.CmdCancelIdReadFailed:
                    return CompleteStatus.IdreadFailed;
                case CancelActionType.CmdNone:
                case CancelActionType.CmdEms:
                case CancelActionType.CmdAbort:
                default:
                    return CompleteStatus.Abort;
            }
        }

        public void AgvcDisconnected()
        {
            try
            {
                SetAlarmFromAgvm(56);
                asePackage.ReportAgvcDisConnect();
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        #region AsePackage

        private void AseBatteryControl_OnBatteryPercentageChangeEvent(object sender, double batteryPercentage)
        {
            try
            {
                Vehicle.BatteryLog.InitialSoc = (int)batteryPercentage;
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public void ReadCarrierId()
        {
            try
            {
                asePackage.ReadCarrierId();
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public void BuzzOff()
        {
            try
            {
                asePackage.BuzzerOff();
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void AsePackage_OnUpdateSlotStatusEvent(object sender, AseCarrierSlotStatus slotStatus)
        {
            try
            {
                if (slotStatus.ManualDeleteCST)
                {
                    slotStatus.CarrierId = "";
                    slotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.Empty;
                    switch (slotStatus.SlotNumber)
                    {
                        case EnumSlotNumber.L:
                            Vehicle.AseCarrierSlotL = slotStatus;
                            break;
                        case EnumSlotNumber.R:
                            Vehicle.AseCarrierSlotR = slotStatus;
                            break;
                    }

                    agvcConnector.Send_Cmd136_CstRemove(slotStatus.SlotNumber);
                }
                else
                {
                    switch (slotStatus.SlotNumber)
                    {
                        case EnumSlotNumber.L:
                            Vehicle.AseCarrierSlotL = slotStatus;
                            break;
                        case EnumSlotNumber.R:
                            Vehicle.AseCarrierSlotR = slotStatus;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        public void AsePackage_OnModeChangeEvent(object sender, EnumAutoState autoState)
        {
            try
            {
                if (Vehicle.AutoState != autoState)
                {
                    switch (autoState)
                    {
                        case EnumAutoState.Auto:
                            StopClearAndReset();
                            asePackage.SetVehicleAutoScenario();
                            ResetAllAlarmsFromAgvm();
                            Vehicle.IsReAuto = true;
                            Thread.Sleep(500);
                            CheckCanAuto();
                            UpdateSlotStatus();
                            break;
                        case EnumAutoState.Manual:
                            StopClearAndReset();
                            asePackage.RequestVehicleToManual();
                            break;
                        case EnumAutoState.None:
                            break;
                        default:
                            break;
                    }
                }

                Vehicle.AutoState = autoState;
                agvcConnector.StatusChangeReport();

                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, $"Switch to {autoState}");
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void CheckCanAuto()
        {

            if (Vehicle.AseMoveStatus.LastSection == null || string.IsNullOrEmpty(Vehicle.AseMoveStatus.LastSection.Id))
            {
                CanAutoMsg = "Section Lost";
                throw new Exception("CheckCanAuto fail. Section Lost.");
            }
            else if (Vehicle.AseMoveStatus.LastAddress == null || string.IsNullOrEmpty(Vehicle.AseMoveStatus.LastAddress.Id))
            {
                CanAutoMsg = "Address Lost";
                throw new Exception("CheckCanAuto fail. Address Lost.");
            }
            else if (Vehicle.AseMoveStatus.AseMoveState != EnumAseMoveState.Idle && Vehicle.AseMoveStatus.AseMoveState != EnumAseMoveState.Block)
            {
                CanAutoMsg = $"Move State = {Vehicle.AseMoveStatus.AseMoveState}";
                throw new Exception($"CheckCanAuto fail. {CanAutoMsg}");
            }
            else if (Vehicle.AseMoveStatus.LastAddress.MyDistance(Vehicle.AseMoveStatus.LastMapPosition) >= Vehicle.MainFlowConfig.InitialPositionRangeMm)
            {
                SetAlarmFromAgvm(54);
                CanAutoMsg = $"Initial Positon Too Far.";
                throw new Exception($"CheckCanAuto fail. {CanAutoMsg}");
            }

            var aseRobotStatus = Vehicle.AseRobotStatus;
            if (aseRobotStatus.RobotState != EnumAseRobotState.Idle)
            {
                CanAutoMsg = $"Robot State = {aseRobotStatus.RobotState}";
                throw new Exception($"CheckCanAuto fail. {CanAutoMsg}");
            }
            else if (!aseRobotStatus.IsHome)
            {
                CanAutoMsg = $"Robot IsHome = {aseRobotStatus.IsHome}";
                throw new Exception($"CheckCanAuto fail. {CanAutoMsg}");
            }

            CanAutoMsg = "OK";
        }

        private void UpdateSlotStatus()
        {
            try
            {
                AseCarrierSlotStatus leftSlotStatus = new AseCarrierSlotStatus(Vehicle.AseCarrierSlotL);
                AseCarrierSlotStatus rightSlotStatus = new AseCarrierSlotStatus(Vehicle.AseCarrierSlotR);

                switch (leftSlotStatus.CarrierSlotStatus)
                {
                    case EnumAseCarrierSlotStatus.Empty:
                        {
                            leftSlotStatus.CarrierId = "";
                            leftSlotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.Empty;
                            Vehicle.AseCarrierSlotL = leftSlotStatus;
                            Vehicle.LeftReadResult = BCRReadResult.BcrNormal;
                        }
                        break;
                    case EnumAseCarrierSlotStatus.Loading:
                        {
                            if (string.IsNullOrEmpty(leftSlotStatus.CarrierId.Trim()))
                            {
                                leftSlotStatus.CarrierId = "ReadFail";
                                leftSlotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.ReadFail;
                                Vehicle.AseCarrierSlotL = leftSlotStatus;
                                Vehicle.LeftReadResult = BCRReadResult.BcrReadFail;
                            }
                            else
                            {
                                Vehicle.LeftReadResult = BCRReadResult.BcrNormal;
                            }
                        }
                        break;
                    case EnumAseCarrierSlotStatus.PositionError:
                        {
                            SetAlarmFromAgvm(51);
                            // AsePackage_OnModeChangeEvent(this, EnumAutoState.Manual);
                        }
                        return;
                    case EnumAseCarrierSlotStatus.ReadFail:
                        {
                            leftSlotStatus.CarrierId = "ReadFail";
                            leftSlotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.ReadFail;
                            Vehicle.AseCarrierSlotL = leftSlotStatus;
                            Vehicle.LeftReadResult = BCRReadResult.BcrReadFail;
                        }
                        break;
                    default:
                        break;
                }

                switch (rightSlotStatus.CarrierSlotStatus)
                {
                    case EnumAseCarrierSlotStatus.Empty:
                        {
                            rightSlotStatus.CarrierId = "";
                            rightSlotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.Empty;
                            Vehicle.AseCarrierSlotR = rightSlotStatus;
                            Vehicle.RightReadResult = BCRReadResult.BcrNormal;
                        }
                        break;
                    case EnumAseCarrierSlotStatus.Loading:
                        {
                            if (string.IsNullOrEmpty(rightSlotStatus.CarrierId.Trim()))
                            {
                                rightSlotStatus.CarrierId = "ReadFail";
                                rightSlotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.ReadFail;
                                Vehicle.AseCarrierSlotR = rightSlotStatus;
                                Vehicle.RightReadResult = BCRReadResult.BcrReadFail;
                            }
                            else
                            {
                                Vehicle.RightReadResult = BCRReadResult.BcrNormal;
                            }
                        }
                        break;
                    case EnumAseCarrierSlotStatus.PositionError:
                        {
                            SetAlarmFromAgvm(51);
                            // AsePackage_OnModeChangeEvent(this, EnumAutoState.Manual);
                        }
                        return;
                    case EnumAseCarrierSlotStatus.ReadFail:
                        {
                            rightSlotStatus.CarrierId = "ReadFail";
                            rightSlotStatus.CarrierSlotStatus = EnumAseCarrierSlotStatus.ReadFail;
                            Vehicle.AseCarrierSlotR = rightSlotStatus;
                            Vehicle.RightReadResult = BCRReadResult.BcrReadFail;
                        }
                        break;
                    default:
                        break;
                }

                agvcConnector.CSTStatusReport(); //200625 dabid#
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void AsePackage_ImportantPspLog(object sender, string msg)
        {
            try
            {
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, msg);
            }
            catch (System.Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void AsePackage_OnAlarmCodeResetEvent(object sender, int e)
        {
            ResetAllAlarmsFromAgvl();
        }

        private void AsePackage_OnAlarmCodeSetEvent1(object sender, int id)
        {
            SetAlarmFromAgvl(id);
        }

        private void AsePackage_OnStatusChangeReportEvent(object sender, string e)
        {
            LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, e);
            agvcConnector.StatusChangeReport();
        }

        #endregion

        #region Set / Reset Alarm

        public void SetAlarmFromAgvm(int errorCode)
        {
            if (!alarmHandler.dicHappeningAlarms.ContainsKey(errorCode))
            {
                alarmHandler.SetAlarm(errorCode);
                asePackage.SetAlarmCode(errorCode, true);
                var IsAlarm = alarmHandler.IsAlarm(errorCode);

                agvcConnector.SetlAlarmToAgvc(errorCode, IsAlarm);
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, alarmHandler.GetAlarmText(errorCode));
            }
        }

        public void SetAlarmFromAgvl(int errorCode)
        {
            if (!alarmHandler.dicHappeningAlarms.ContainsKey(errorCode))
            {
                alarmHandler.SetAlarm(errorCode);
                var IsAlarm = alarmHandler.IsAlarm(errorCode);
                agvcConnector.SetlAlarmToAgvc(errorCode, IsAlarm);
                LogDebug(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, alarmHandler.GetAlarmText(errorCode));
            }
        }

        public void ResetAllAlarmsFromAgvm()
        {
            alarmHandler.ResetAllAlarms();
            asePackage.ResetAllAlarmCode();
            agvcConnector.ResetAllAlarmsToAgvc();
        }

        public void ResetAllAlarmsFromAgvc()
        {
            alarmHandler.ResetAllAlarms();
            asePackage.ResetAllAlarmCode();
        }

        public void ResetAllAlarmsFromAgvl()
        {
            alarmHandler.ResetAllAlarms();
            agvcConnector.ResetAllAlarmsToAgvc();
        }

        #endregion

        #region Log

        public void AppendDebugLog(string msg)
        {
            try
            {
                lock (DebugLogMsg)
                {
                    DebugLogMsg = string.Concat(DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss.fff"), "\t", msg, "\r\n", DebugLogMsg);

                    if (DebugLogMsg.Length > 65535)
                    {
                        DebugLogMsg = DebugLogMsg.Substring(65535);
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(GetType().Name + ":" + MethodBase.GetCurrentMethod().Name, ex.Message);
            }
        }

        private void LogException(string classMethodName, string exMsg)
        {
            try
            {
                mirleLogger.Log(new LogFormat("Error", "5", classMethodName, Vehicle.AgvcConnectorConfig.ClientName, "CarrierID", exMsg));
            }
            catch (Exception) { }
        }

        public void LogDebug(string classMethodName, string msg)
        {
            try
            {
                mirleLogger.Log(new LogFormat("Debug", "5", classMethodName, Vehicle.AgvcConnectorConfig.ClientName, "CarrierID", msg));
                AppendDebugLog(msg);
            }
            catch (Exception) { }
        }

        private void LogMsgHandler(object sender, LogFormat e)
        {
            mirleLogger.Log(e);
        }

        #endregion
    }

    public class LastIdlePosition
    {
        public DateTime TimeStamp { get; set; } = DateTime.Now;
        public MapPosition Position { get; set; } = new MapPosition();
    }
}