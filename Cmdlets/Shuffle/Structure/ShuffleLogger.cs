namespace Innofactor.Crm.CI.Cmdlets.Structure
{
    using Xrm.Utils.Core.Common.Interfaces;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Reflection;

    /// <summary>
    /// Implementation of ILoggable for Shuffle
    /// </summary>
    internal class ShuffleLogger : ILoggable
    {
        #region Private Fields

        private readonly DateTime begin;

        //private string name;
        private readonly XrmCmdletBase cmdlet;

        /// <summary>
        ///
        /// </summary>
        protected List<Tuple<string, DateTime>> stack = new List<Tuple<string, DateTime>>();

        #endregion Private Fields

        #region Public Constructors

        public ShuffleLogger(XrmCmdletBase cmdlet)
        {
            begin = DateTime.Now;
            this.cmdlet = cmdlet;
        }

        #endregion Public Constructors

        #region Public Methods

        public void CloseLog() =>
            CloseLog(string.Empty);

        public void CloseLog(string closetext)
        {
            var now = DateTime.Now;
            var timeString = string.Format(
                "at {0}, {1}:{2:d2} elapsed.",
                now.ToShortTimeString(),
                (int)now.Subtract(begin).TotalMinutes,
                now.Subtract(begin).Seconds);
            if (!string.IsNullOrEmpty(closetext))
            {
                Console.Title = $"Shuffle - {closetext}";
            }

            cmdlet.WriteVerbose($"Finished {timeString}");
        }

        public void EndSection()
        {
            if(stack.Count == 0) 
                return; 
            int i = stack.Count - 1;
            try
            {
                var section = stack[i];
                stack.RemoveAt(i);
                cmdlet.WriteVerbose($" [END of {section.Item1}] ({(DateTime.Now - section.Item2).TotalMilliseconds})");
            }
            catch (ArgumentOutOfRangeException)
            {
                cmdlet.WriteDebug($"  *** Logger: Invalid section stack index: {i}");
                cmdlet.WriteDebug("[END of  -unknown section-]");
            }
        }

        public void Log(string message) =>
           cmdlet.WriteDebug($" [INFO] {message}");

        public void Log(Exception ex) => LogException(ex);

        public void Log(string message, params object[] arg) =>
          cmdlet.WriteVerbose(" [INFO] " + string.Format(message, arg));

        public void StartSection(string section = null)
        {
            if (string.IsNullOrEmpty(section))
            {
                var mb = GetOrigin();
                section = mb.ReflectedType.Name + "." + mb.Name;
            }
            cmdlet.WriteVerbose($" [START of {section}]");
            stack.Add(new Tuple<string, DateTime>(section, DateTime.Now));
        }

        #endregion Public Methods

        #region Private Methods

        private MethodBase GetOrigin()
        {
            StackFrame[] stackFrames = new StackTrace(true).GetFrames();
            MethodBase mb = null;
            try
            {
                foreach (StackFrame stackFrame in stackFrames)
                {
                    mb = stackFrame.GetMethod();
                    if (!mb.ReflectedType.FullName.ToLowerInvariant().StartsWith("innofactor.xrm.utils"))
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) { }
            return mb;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="ex"></param>
        private void LogException(Exception ex)
        {
            var padding = Indentchars();
            cmdlet.WriteDebug("---------------------------------------------------------");
            cmdlet.WriteDebug($"{padding}{ex.ToString()}");
            cmdlet.WriteDebug($"{padding}{ex.Message}");
            cmdlet.WriteDebug($"{padding}{ex.Source}");
            cmdlet.WriteDebug($"{padding}{ex.StackTrace}");
            cmdlet.WriteDebug("---------------------------------------------------------");
        }
        #endregion Private Methods
        /// <summary>
        /// Returns current indentation based on section stack count
        /// </summary>
        /// <returns></returns>
        protected string Indentchars()
        {
            return new string(' ', (stack.Count > 0) ? stack.Count * 2 : 0); ;
        }
    }
}