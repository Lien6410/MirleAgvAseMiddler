﻿using Mirle.Agv.Controller;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Mirle.Agv.Model.TransferCmds;
using Mirle.Agv.Model;

namespace Mirle.Agv.View
{
    public partial class ManualMoveCmdForm : Form
    {
        private MainFlowHandler mainFlowHandler;
        private MoveCmdInfo moveCmdInfo;
        private MapInfo theMapInfo;

        public ManualMoveCmdForm(MainFlowHandler mainFlowHandler, MapInfo theMapInfo)
        {
            InitializeComponent();
            this.mainFlowHandler = mainFlowHandler;
            this.theMapInfo = theMapInfo;
            this.moveCmdInfo = new MoveCmdInfo(theMapInfo);
            ListMapAddressPositionsAddItems();
            ListMapAddressActionsAddItems();
            ListMapSpeedLimitsAddItems();
        }

        private void ListMapAddressPositionsAddItems()
        {
            listMapAddressPositions.Items.Clear();
            foreach (var valuePair in theMapInfo.allMapAddresses)
            {
                MapAddress mapAddress = valuePair.Value;
                MapPosition mapPosition = mapAddress.Position.DeepClone();
                string txtPosition = $"{mapPosition.X},{mapPosition.Y}";
                listMapAddressPositions.Items.Add(txtPosition);
            }
        }

        private void ListMapAddressActionsAddItems()
        {
            listMapAddressActions.Items.Clear();
            foreach (string item in Enum.GetNames(typeof(EnumAddressAction)))
            {
                listMapAddressActions.Items.Add(item);
            }
        }

        private void ListMapSpeedLimitsAddItems()
        {
            listMapSpeedLimits.Items.Clear();
            Dictionary<float, short> dicSpeedLimits = new Dictionary<float, short>();
            foreach (var valuePair in theMapInfo.allMapSections)
            {
                MapSection mapSection = valuePair.Value;
                float speedLimit = mapSection.Speed;
                if (!dicSpeedLimits.ContainsKey(speedLimit))
                {
                    dicSpeedLimits.Add(speedLimit, 1);
                    listMapSpeedLimits.Items.Add(speedLimit);
                }
            }
        }

        private void btnAddAddressPosition_Click(object sender, EventArgs e)
        {
            if (listMapAddressPositions.Items.Count < 1)
            {
                return;
            }
            if (listMapAddressPositions.SelectedIndex < 0)
            {
                listMapAddressPositions.SelectedIndex = 0;
            }

            listCmdAddressPositions.Items.Add(listMapAddressPositions.SelectedItem);
        }

        private void btnRemoveLastAddressPosition_Click(object sender, EventArgs e)
        {
            if (listCmdAddressPositions.Items.Count < 1)
            {
                return;
            }
            if (listCmdAddressPositions.SelectedIndex < 0)
            {
                listCmdAddressPositions.SelectedIndex = listCmdAddressPositions.Items.Count - 1;
            }

            listCmdAddressPositions.Items.RemoveAt(listCmdAddressPositions.SelectedIndex);
        }

        private void btnAddressPositionsClear_Click(object sender, EventArgs e)
        {
            listCmdAddressPositions.Items.Clear();
        }

        private void btnAddAddressAction_Click(object sender, EventArgs e)
        {
            if (listMapAddressActions.Items.Count < 1)
            {
                return;
            }
            if (listMapAddressActions.SelectedIndex < 0)
            {
                listMapAddressActions.SelectedIndex = 0;
            }

            listCmdAddressActions.Items.Add(listMapAddressActions.SelectedItem);
        }

        private void btnRemoveLastAddressAction_Click(object sender, EventArgs e)
        {
            if (listCmdAddressActions.Items.Count < 1)
            {
                return;
            }
            if (listCmdAddressActions.SelectedIndex < 0)
            {
                listCmdAddressActions.SelectedIndex = listCmdAddressActions.Items.Count - 1;
            }

            listCmdAddressActions.Items.RemoveAt(listCmdAddressActions.SelectedIndex);
        }

        private void btnClearAddressActions_Click(object sender, EventArgs e)
        {
            listCmdAddressActions.Items.Clear();
        }

        private void btnAddSpeedLimit_Click(object sender, EventArgs e)
        {
            if (listMapSpeedLimits.Items.Count < 1)
            {
                return;
            }
            if (listMapSpeedLimits.SelectedIndex < 0)
            {
                listMapSpeedLimits.SelectedIndex = 0;
            }

            listCmdSpeedLimits.Items.Add(listMapSpeedLimits.SelectedItem);
        }

        private void btnRemoveSpeedLimit_Click(object sender, EventArgs e)
        {
            if (listCmdSpeedLimits.Items.Count < 1)
            {
                return;
            }
            if (listCmdSpeedLimits.SelectedIndex < 0)
            {
                listCmdSpeedLimits.SelectedIndex = listCmdSpeedLimits.Items.Count - 1;
            }

            listCmdSpeedLimits.Items.RemoveAt(listCmdSpeedLimits.SelectedIndex);

        }

        private void btnClearSpeedLimit_Click(object sender, EventArgs e)
        {
            listCmdSpeedLimits.Items.Clear();
        }

        private void btnClearMoveCmdInfo_Click(object sender, EventArgs e)
        {
            moveCmdInfo = new MoveCmdInfo(theMapInfo);
        }

        private void btnCheckMoveCmdInfo_Click(object sender, EventArgs e)
        {
            WarningForm warningForm = new WarningForm();
            warningForm.TopLevel = true;

            if (moveCmdInfo == null)
            {
                warningForm.WarningMsg = "Move Command is null";
                warningForm.Show();
                return;
            }
            var positions = moveCmdInfo.AddressPositions;
            if (positions.Count < 2)
            {
                warningForm.WarningMsg = "AddressPositionList.Count Error";
                warningForm.Show();
                return;
            }
            var actions = moveCmdInfo.AddressActions;
            if (actions.Count < 2)
            {
                warningForm.WarningMsg = "AddressActionList.Count Error";
                warningForm.Show();
                return;
            }

            //Cancel Repeat
            for (int i = 0; i < positions.Count - 1; i++)
            {
                if (RepeatNext(positions[i], positions[i + 1]))
                {
                    positions.RemoveAt(i);
                    actions.RemoveAt(i);
                    btnCheckMoveCmdInfo_Click(sender, e);
                }
            }

            if (actions.Count != positions.Count)
            {
                warningForm.WarningMsg = "AddressPositionList and AddressActionList are not match";
                warningForm.Show();
                return;
            }
            if (actions[actions.Count - 1] != EnumAddressAction.End)
            {
                warningForm.WarningMsg = "AddressActionList is not end with EnumAddressAction.End";
                warningForm.Show();
                return;
            }
            var limits = moveCmdInfo.SectionSpeedLimits;
            if (limits.Count + 1 != positions.Count)
            {
                warningForm.WarningMsg = "SpeedLimitList.Count Error";
                warningForm.Show();
                return;
            }

            warningForm.WarningMsg = "MoveCommand legal.";
            warningForm.Show();
        }

        private bool RepeatNext(MapPosition pos1, MapPosition pos2)
        {
            if (pos1.X == pos2.X)
            {
                if (pos1.Y == pos2.Y)
                {
                    return true;
                }
            }
            return false;
        }

        private void btnSendMoveCmdInfo_Click(object sender, EventArgs e)
        {
            SetMoveCmdInfo();
            mainFlowHandler.PublishTransferMoveEvent(moveCmdInfo);
        }

        private void SetMoveCmdInfo()
        {
            SetPositions();
            SetActions();
            SetSpeedLimits();

            //TODO: moveCmdInfo from this -> mainFlow -> moveController -> do move command
            // mainFlowHandler.PublishTransferMoveEvent(moveCmdInfo);
        }

        private void SetSpeedLimits()
        {
            moveCmdInfo.SectionSpeedLimits.Clear();
            for (int i = 0; i < listCmdSpeedLimits.Items.Count; i++)
            {
                float limit = (float)listCmdSpeedLimits.Items[i];
                moveCmdInfo.SectionSpeedLimits.Add(limit);
            }
        }

        private void SetActions()
        {
            moveCmdInfo.AddressActions.Clear();
            for (int i = 0; i < listCmdAddressActions.Items.Count; i++)
            {
                string strAction = (string)listCmdAddressActions.Items[i];
                EnumAddressAction action = (EnumAddressAction)Enum.Parse(typeof(EnumAddressAction), strAction);
                moveCmdInfo.AddressActions.Add(action);
            }
        }

        private void SetPositions()
        {
            moveCmdInfo.AddressPositions.Clear();
            for (int i = 0; i < listCmdAddressPositions.Items.Count; i++)
            {
                string positionPair = (string)listCmdAddressPositions.Items[i];
                string[] posXY = positionPair.Split(',');
                var posX = float.Parse(posXY[0]);
                var posY = float.Parse(posXY[1]);
                moveCmdInfo.AddressPositions.Add(new MapPosition(posX, posY));
            }
        }

        private void btnSetIds_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtCmdId.Text))
            {
                moveCmdInfo.CmdId = txtCmdId.Text;
            }

            if (!string.IsNullOrEmpty(txtCstId.Text))
            {
                moveCmdInfo.CstId = txtCstId.Text;
            }
        }

        private void btnClearIds_Click(object sender, EventArgs e)
        {
            string cmdId = "Cmd001";
            string cstId = "Cst001";
            moveCmdInfo.CmdId = cmdId;
            moveCmdInfo.CstId = cstId;
            txtCmdId.Text = cmdId;
            txtCstId.Text = cstId;
        }

        private void btnPositionXY_Click(object sender, EventArgs e)
        {
            string txtPosition = $"{numPositionX.Value},{numPositionY.Value}";         
            listCmdAddressPositions.Items.Add(txtPosition);
        }

        private void btnStopVehicle_Click(object sender, EventArgs e)
        {
            mainFlowHandler.StopVehicle();
        }
    }
}
