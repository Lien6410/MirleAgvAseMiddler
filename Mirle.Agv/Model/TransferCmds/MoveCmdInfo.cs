﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mirle.Agv.Control;

namespace Mirle.Agv.Model.TransferCmds
{
    public class MoveCmdInfo : TransCmd
    {

        //public MapSection Section { get; set; }
        //public bool IsPrecisePositioning { get; set; }  //是否二次定位 // = IsMoveEndSection 本次連續移動最後一個Section

        public List<MapPosition> AddressPositions { get; set; } = new List<MapPosition>();
        public List<EnumAddressAction> AddressActions { get; set; } = new List<EnumAddressAction>();
        public List<float> SectionSpeedLimits { get; set; } = new List<float>();
        public int PredictVehicleAngle { get; set; } = 0;

        public List<MapSection> movingSections = new List<MapSection>();
        public int MovingSectionsIndex { get; set; } = 0;

        public MoveCmdInfo() : base()
        {
            type = EnumTransCmdType.Move;
        }

        public List<MapPosition> SetAddressPositions(string[] addresses)
        {
            List<MapPosition> result = new List<MapPosition>();
            try
            {
                for (int i = 0; i < addresses.Length; i++)
                {
                    MapAddress mapAddress = theMapInfo.dicMapAddresses[addresses[i]];
                    MapPosition position = mapAddress.GetPosition();
                    result.Add(position);
                }
            }
            catch (Exception ex)
            {
                var msg = ex.StackTrace;
            }
            return result;
        }

        public List<float> SetSectionSpeedLimits(string[] sections)
        {
            List<float> result = new List<float>();
            try
            {
                for (int i = 0; i < sections.Length; i++)
                {
                    MapSection mapSection = theMapInfo.dicMapSections[sections[i]];
                    float SpeedLimit = mapSection.Speed;
                    result.Add(SpeedLimit);
                }
            }
            catch (Exception ex)
            {
                var msg = ex.StackTrace;
            }
            return result;
        }

        public List<EnumAddressAction> SetAddressActions(string[] sections)
        {
            PredictVehicleAngle = 0;
            List<EnumAddressAction> result = new List<EnumAddressAction>();
            try
            {
                MapSection firstSection = theMapInfo.dicMapSections[sections[0]];
                if (firstSection.Type == EnumSectionType.R2000)
                {
                    result.Add(EnumAddressAction.R2000);
                }
                else
                {
                    result.Add(EnumAddressAction.ST);
                }

                for (int i = 0; i < sections.Length - 1; i++)
                {
                    MapSection currentSection = theMapInfo.dicMapSections[sections[i]];
                    MapSection nextSection = theMapInfo.dicMapSections[sections[i + 1]];
                    EnumAddressAction addressMotion = SetAddressMotion(currentSection, nextSection);
                    result.Add(addressMotion);
                }
            }
            catch (Exception ex)
            {
                var msg = ex.StackTrace;
            }
            result.Add(EnumAddressAction.End);
            return result;
        }

        private EnumAddressAction SetAddressMotion(MapSection currentSection, MapSection nextSection)
        {
            if (nextSection.Type == EnumSectionType.R2000)
            {
                //水平接R2000 或是 垂直接R2000 是否不同
                return EnumAddressAction.R2000;
            }
            else if (currentSection.Type == EnumSectionType.R2000)
            {
                //R2000接水平 或是 R2000接垂直 是否不同
                return EnumAddressAction.ST;
            }
            else if (currentSection.Type == nextSection.Type)
            {
                //水平接水平 或 垂直接垂直
                return EnumAddressAction.ST;
            }
            else
            {
                //水平接垂直 或 垂直接水平
                if (IsTurnRight(currentSection, nextSection))
                {
                    //右轉
                    PredictVehicleAngle -= 90;
                    if (PredictVehicleAngle < -100)
                    {
                        PredictVehicleAngle = 0;
                        return EnumAddressAction.BTR;
                    }
                    return EnumAddressAction.TR;
                }
                else
                {
                    //左轉
                    PredictVehicleAngle += 90;
                    if (PredictVehicleAngle > 100)
                    {
                        PredictVehicleAngle = 0;
                        return EnumAddressAction.BTR;
                    }
                    return EnumAddressAction.TR;
                }
            }
        }

        private bool IsTurnRight(MapSection currentSection, MapSection nextSection)
        {
            MapAddress mapAddressA = theMapInfo.dicMapAddresses[currentSection.FromAddress];
            MapAddress mapAddressB = theMapInfo.dicMapAddresses[currentSection.ToAddress];
            MapAddress mapAddressC = theMapInfo.dicMapAddresses[nextSection.ToAddress];

            MapPosition positionA = mapAddressA.GetPosition();
            MapPosition positionB = mapAddressB.GetPosition();
            MapPosition positionC = mapAddressC.GetPosition();

            if (positionA.PositionX == positionB.PositionX)
            {
                //垂直接水平
                if (positionA.PositionY < positionB.PositionY)
                {
                    //北往南
                    if (positionB.PositionX < positionC.PositionX)
                    {
                        //北往南 接著 西往東 = 左轉
                        return false;
                    }
                    else
                    {
                        //北往南 接著 東往西 = 右轉
                        return true;
                    }
                }
                else
                {
                    //南往北
                    if (positionB.PositionX < positionC.PositionX)
                    {
                        //南往北 接著 西往東 = 右轉
                        return true;
                    }
                    else
                    {
                        //南往北 接著 東往西 = 左轉
                        return false;
                    }
                }
            }
            else
            {
                //水平接垂直
                if (positionA.PositionX < positionB.PositionX)
                {
                    //西往東
                    if (positionB.PositionY < positionC.PositionY)
                    {
                        //西往東 接 北往南 = 右轉
                        return true;
                    }
                    else
                    {
                        //西往東 接 南往北 = 左轉
                        return false;
                    }
                }
                else
                {
                    //東往西
                    if (positionB.PositionY < positionC.PositionY)
                    {
                        //東往西 接 北往南 = 左轉
                        return false;
                    }
                    else
                    {
                        //東往西 接 南往北 = 右轉
                        return true;
                    }
                }
            }
        }
    }
}
