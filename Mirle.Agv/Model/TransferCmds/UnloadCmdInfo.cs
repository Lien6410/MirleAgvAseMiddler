﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mirle.Agv.Control.Handler;

namespace Mirle.Agv.Model.TransferCmds
{
    public class UnloadCmdInfo : TransCmd
    {
        public string UnloadAddress { get; set; }
        public string CassetteId { get; set; }
        public int StageNum { get; set; }

        public UnloadCmdInfo(ITransferHandler transferHandler) : base(transferHandler)
        {
            type = EnumPartialJobType.Unload;
        }
    }
}
