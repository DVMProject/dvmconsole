// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/

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
