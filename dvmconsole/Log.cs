// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2025 Bryan Biedenkapp, N2PLL
*
*/

using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace dvmconsole
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public delegate void LogWriteLine(string message);

    /// <summary>
    /// Implements the logger system.
    /// </summary>
    public class Log
    {
        public static FileStream logStream = null;
        /// <summary>
        /// Flag indicating logging should display on a console window.
        /// </summary>
        public static bool DisplayToConsole = false;
        private static TextWriter tw;
        private const string LOG_TIME_FORMAT = "MM/dd/yyyy HH:mm:ss";

        private static ConsoleColor defColor = Console.ForegroundColor;

        /// <summary>
        /// Flag indicating logging should stop.
        /// </summary>
        public static bool StopLogging = false;

        /*
        ** Properties
        */

        /// <summary>
        /// Gets or sets a delegate to also write logs to.
        /// </summary>
        public static LogWriteLine LogWriter { get; set; } = null;

        /*
        ** Methods
        */

        /// <summary>
        /// Sets up the trace logging.
        /// </summary>
        /// <param name="directoryPath"></param>
        /// <param name="logFile"></param>
        public static void SetupTextWriter(string directoryPath, string logFile)
        {
            // destroy existing log file
            if (File.Exists(directoryPath + Path.DirectorySeparatorChar + logFile))
                File.Delete(directoryPath + Path.DirectorySeparatorChar + logFile);

            // open a new log stream
            logStream = new FileStream(directoryPath + Path.DirectorySeparatorChar + logFile, FileMode.CreateNew);
            tw = new StreamWriter(logStream);
        }

        /// <summary>
        /// Writes a log entry to the text log.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="noTimeStamp"></param>
        public static void WriteLog(string message, bool noTimeStamp = false)
        {
            string logTime = DateTime.Now.ToString(LOG_TIME_FORMAT) + " ";
            if (tw != null)
            {
                if (noTimeStamp)
                    tw.WriteLine(message);
                else
                    tw.WriteLine(logTime + message);
                tw.Flush();
            }

            if (noTimeStamp)
                System.Diagnostics.Trace.WriteLine(message);
            else
                System.Diagnostics.Trace.WriteLine(logTime + message);
            if (LogWriter != null)
                LogWriter(message);
            if (DisplayToConsole)
            {
                if (!noTimeStamp)
                    Console.Write(logTime);

                if (message.StartsWith("WARN"))
                    Console.ForegroundColor = ConsoleColor.Yellow;
                else if (message.StartsWith("FAIL") || message.StartsWith("ERROR"))
                    Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine(message);

                Console.ForegroundColor = defColor;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="dynamic"></param>
        /// <returns></returns>
        public static string GenericToString<T>(T value, bool dynamic)
        {
            return GenericToString<T>(value, dynamic, ", ");
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <param name="dynamic"></param>
        /// <param name="separator"></param>
        /// <returns></returns>
        public static string GenericToString<T>(T value, bool dynamic, string separator)
        {
            return GenericToString<T>(dynamic ? value.GetType() : typeof(T), ref value, separator);
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type"></param>
        /// <param name="value"></param>
        /// <param name="separator"></param>
        /// <returns></returns>
        private static string GenericToString<T>(Type type, [In] ref T value, string separator)
        {
            var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            List<string> strs = new List<string>();
            foreach (var property in properties)
            {
                if (property.CanRead)
                {
                    try { strs.Add(string.Format("{0} = {1}", property.Name, property.GetValue(value, null))); }
                    catch (InvalidOperationException) { }
                }
            }
            return string.Format("{0} {{{1}}}", type.Name, string.Join(separator, strs.ToArray()));
        }

        /// <summary>
        /// Writes the exception stack trace to the console/trace log
        /// </summary>
        /// <param name="throwable">Exception to obtain information from</param>
        /// <param name="reThrow"></param>
        public static void StackTrace(Exception throwable, bool reThrow = true)
        {
            StackTrace(string.Empty, throwable, reThrow);
        }

        /// <summary>
        /// Writes the exception stack trace to the console/trace log
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="throwable">Exception to obtain information from</param>
        /// <param name="reThrow"></param>
        public static void StackTrace(string msg, Exception throwable, bool reThrow = true)
        {
            MethodBase mb = new System.Diagnostics.StackTrace().GetFrame(1).GetMethod();
            ParameterInfo[] param = mb.GetParameters();
            string funcParams = string.Empty;
            for (int i = 0; i < param.Length; i++)
                if (i < param.Length - 1)
                    funcParams += param[i].ParameterType.Name + ", ";
                else
                    funcParams += param[i].ParameterType.Name;

            Exception inner = throwable.InnerException;

            WriteError("caught an unrecoverable exception! " + msg);
            WriteLog("---- TRACE SNIP ----");
            WriteLog(throwable.Message + (inner != null ? " (Inner: " + inner.Message + ")" : ""));
            WriteLog(throwable.GetType().ToString());

            WriteLog("<" + mb.ReflectedType.Name + "::" + mb.Name + "(" + funcParams + ")>");
            WriteLog(throwable.Source);
            foreach (string str in throwable.StackTrace.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
                WriteLog(str);
            if (inner != null)
                foreach (string str in throwable.StackTrace.Split(new string[] { Environment.NewLine }, StringSplitOptions.None))
                    WriteLog("inner trace: " + str);
            WriteLog("---- TRACE SNIP ----");

            if (reThrow)
                throw throwable;
        }

        /// <summary>
        /// Writes a error trace message w/ calling function information.
        /// </summary>
        /// <param name='message'>Message to print</param>
        public static void WriteWarning(string message)
        {
            WriteLog("WARN: " + message);
        }

        /// <summary>
        /// Writes a error trace message w/ calling function information.
        /// </summary>
        /// <param name='message'>Message to print</param>
        public static void WriteError(string message)
        {
            WriteLog("ERROR: " + message);
        }

        /// <summary>
        /// Writes a trace message w/ calling function information.
        /// </summary>
        /// <param name="message">Message to print to debug window</param>
        /// <param name="myTraceFilter"></param>
        /// <param name="frame"></param>
        /// <param name="dropToConsole"></param>
        /// <param name="noTimeStamp"></param>
        public static void WriteLine(string message, int frame = 1, bool dropToConsole = false, bool noTimeStamp = false)
        {
            string trace = string.Empty;

            MethodBase mb = new System.Diagnostics.StackTrace().GetFrame(frame).GetMethod();
            ParameterInfo[] param = mb.GetParameters();
            string funcParams = string.Empty;
            for (int i = 0; i < param.Length; i++)
                if (i < param.Length - 1)
                    funcParams += param[i].ParameterType.Name + ", ";
                else
                    funcParams += param[i].ParameterType.Name;

            trace += "<" + mb.ReflectedType.Name + "::" + mb.Name + "(" + funcParams + ")> ";
            trace += message;

            WriteLog(trace, noTimeStamp);
        }

        /// <summary>
        /// Helper to display the ASCII representation of a hex dump.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private static string DisplayHexChars(byte[] buffer, int offset)
        {
            int bCount = 0;

            string _out = string.Empty;
            for (int i = offset; i < buffer.Length; i++)
            {
                // stop every 16 bytes...
                if (bCount == 16)
                    break;

                byte b = buffer[i];
                char c = Convert.ToChar(b);

                // make control and illegal characters spaces
                if (c >= 0x00 && c <= 0x1F)
                    c = ' ';
                if (c >= 0x7F)
                    c = ' ';

                _out += c;

                bCount++;
            }

            return _out;
        }

        /// <summary>
        /// Perform a hex dump of a buffer.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="buffer"></param>
        /// <param name="maxLength"></param>
        /// <param name="myTraceFilter"></param>
        /// <param name="startOffset"></param>
        /// <param name="dropToConsole"></param>
        public static void TraceHex(string message, byte[] buffer, int maxLength = 32, int startOffset = 0)
        {
            int bCount = 0, j = 0, lenCount = 0;

            // iterate through buffer printing all the stored bytes
            string traceMsg = message + "\nDUMP " + j.ToString("X4") + ": ";
            for (int i = startOffset; i < buffer.Length; i++)
            {
                byte b = buffer[i];

                // split the message every 16 bytes...
                if (bCount == 16)
                {
                    traceMsg += "\t*" + DisplayHexChars(buffer, j) + "*";
                    WriteLine(traceMsg, 2, false, true);

                    bCount = 0;
                    j += 16;
                    traceMsg = "DUMP " + j.ToString("X4") + ": ";
                }
                else
                    traceMsg += (bCount > 0) ? " " : "";

                traceMsg += b.ToString("X2");

                bCount++;

                // increment the length counter, and check if we've exceeded the specified
                // maximum, then break the loop
                lenCount++;
                if (lenCount > maxLength)
                    break;
            }

            // if the byte count at this point is non-zero print the message
            if (bCount != 0)
            {
                traceMsg += "\t*" + DisplayHexChars(buffer, j) + "*";
                WriteLine(traceMsg, 2, false, true);
            }
        }
    } // public class Log
} // namespace dvmconsole
