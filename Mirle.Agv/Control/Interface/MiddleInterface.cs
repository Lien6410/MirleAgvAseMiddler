﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mirle.Agv.Model;
using Mirle.Agv.Control.Tools;
using Mirle.Agv.Model.TransferCmds;

namespace Mirle.Agv.Control
{
    public class MiddleInterface : IMapBarcodeTaker
    {
        private List<TransCmd> transCmds;

        public MiddleInterface()
        {
            transCmds = new List<TransCmd>();
        }

        public void WhenGetAgvcTransCmd(AgvcTransCmd agvcTransCmd)
        {
            //TODO : 
            //1. Convert agvcTransCmd into a list of (SomeCmd imp TransCmd) and set to the transCmds
            //2. Send transCmds to mainflow.
            throw new NotImplementedException();
        }

        public bool GetReserveFromAgvc(string sectionId)
        {
            throw new NotImplementedException();
        }

        public void UpdateMapBarcode(MapBarcodeValues mapBarcode)
        {
            //TODO: Make a Position change report from mapBarcode and send to AGVC
            throw new NotImplementedException();
        }
    }
}
