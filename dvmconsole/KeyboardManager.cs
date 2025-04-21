// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Steven Jennison, KD8RHO
*
*/

using System.Collections.ObjectModel;
using System.Windows.Forms;

namespace dvmconsole;

public class KeyboardManager
{
    
    /*
    ** Properties
    */
    public bool IsListening { get; private set; } = false;

    public event Action<Keys,GlobalKeyboardHook.KeyboardState> OnKeyEvent;

    private GlobalKeyboardHook listenHook;
    private GlobalKeyboardHook.HookProc hookProcHandle;
    /*
    ** Methods
    */
    public void SetListenKeys(List<Keys> keys)
    {
        if (listenHook == null)
        {
            listenHook = new GlobalKeyboardHook(keys.ToArray());
            listenHook.KeyboardPressed += ListenHookOnKeyboardEvent;
            hookProcHandle = GlobalKeyboardHook.HookProcHandle;
        }
        else
        {
            listenHook.RegisteredKeys = keys.ToArray();
        }
    }

    private void ListenHookOnKeyboardEvent(object sender, GlobalKeyboardHookEventArgs e)
    {
        OnKeyEvent?.Invoke(e.KeyboardData.Key, e.KeyboardState);
    }


    /// <summary>
    /// Gets the next key pressed globally, for use with user dialogs
    /// </summary>
    /// <returns>The next key pressed</returns>
    public async Task<Keys> GetNextKeyPress()
    {
        GlobalKeyboardHook universalHook = new GlobalKeyboardHook();
        Keys? result = null;

        universalHook.KeyboardPressed += onUniversalHookOnKeyboardPressed;
        while (result == null)
        {
            await Task.Delay(100);
        }
        
        universalHook.KeyboardPressed -= onUniversalHookOnKeyboardPressed;
        return result.Value;

        void onUniversalHookOnKeyboardPressed(object sender, GlobalKeyboardHookEventArgs args)
        {
            if (args.KeyboardState == GlobalKeyboardHook.KeyboardState.KeyDown)
            {
                result = args.KeyboardData.Key;
                // Stop listening for key presses on first key pressed
                universalHook.KeyboardPressed -= onUniversalHookOnKeyboardPressed;
            }
        }
        
    }
}