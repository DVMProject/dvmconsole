// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (C) 2026 C. Lovell, Dev_Ranger
using fnecore;

namespace dvmconsole
{
    public abstract partial class FneSystemBase
    {
        protected override void AnalogDataReceived(object sender, AnalogDataReceivedEvent e)
        {
            if (e.CallType == CallType.GROUP)
                mainWindow.AnalogDataReceived((this as PeerSystem)?.ConfiguredSystemName, e, DateTime.Now);
        }
    }
}
