﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mirle.Agv.Model
{
    public class MoveCommandData
    {
        public List<Command> CommandList { get; set; }
        public int IndexOfCmdList { get; set; }

        public List<SectionLine> SectionLineList { get; set; }
        public int IndexOflisSectionLine { get; set; }

        public List<ReserveData> ReserveList { get; set; }
        public int IndexOfReserveList { get; set; }

        public MapPosition End { get; set; }
        public double EndOffsetX { get; set; }
        public double EndOffsetY { get; set; }
        public double EndOffsetTheta { get; set; }
        public double StartOffsetX { get; set; }
        public double StartOffsetY { get; set; }
        public double StartOffsetTheta { get; set; }
        public bool EndAddressLoadUnload { get; set; }

        public MoveCommandData()
        {
            CommandList = new List<Command>();
            IndexOfCmdList = 0;

            SectionLineList = new List<SectionLine>();
            IndexOflisSectionLine = 0;

            ReserveList = new List<ReserveData>();
            IndexOfReserveList = 0;

            EndOffsetX = 0;
            EndOffsetY = 0;
            EndOffsetTheta = 0;
            StartOffsetX = 0;
            StartOffsetY = 0;
            StartOffsetTheta = 0;
            EndAddressLoadUnload = false;
        }

        public MoveCommandData(List<Command> commandList, List<SectionLine> sectionLineList, List<ReserveData> reserveList)
        {
            CommandList = commandList;
            IndexOfCmdList = 0;

            SectionLineList = sectionLineList;
            IndexOflisSectionLine = 0;

            ReserveList = reserveList;
            IndexOfReserveList = 0;

            EndOffsetX = 0;
            EndOffsetY = 0;
            EndOffsetTheta = 0;
            StartOffsetX = 0;
            StartOffsetY = 0;
            StartOffsetTheta = 0;
            EndAddressLoadUnload = false;
        }
    }
}
