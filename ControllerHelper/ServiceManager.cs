﻿using ControllerCommon;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Timers;
using Timer = System.Timers.Timer;

namespace ControllerHelper
{
    public class ServiceManager
    {
        private string name;
        private string display;
        private string description;

        private ServiceController controller;
        private ServiceControllerStatus status;
        private int prevStatus = -1;
        private ServiceControllerStatus nextStatus;
        private ServiceStartMode starttype;

        private Process process;

        private Timer MonitorTimer;
        private object updateLock = new();

        private readonly ControllerHelper helper;
        private readonly ILogger logger;

        public ServiceManager(string name, ControllerHelper helper, string display, string description, ILogger logger)
        {
            this.helper = helper;
            this.logger = logger;

            this.name = name;
            this.display = display;
            this.description = description;

            this.controller = new ServiceController(name);

            process = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    FileName = @"C:\Windows\system32\sc.exe",
                    Verb = "runas"
                }
            };

            // monitor service
            MonitorTimer = new Timer(1000) { Enabled = true, AutoReset = true };
            MonitorTimer.Elapsed += MonitorHelper;
        }

        private void MonitorHelper(object sender, ElapsedEventArgs e)
        {
            lock (updateLock)
            {
                // refresh service status
                try
                {
                    controller.Refresh();
                    status = controller.Status;
                    starttype = controller.StartType;
                }
                catch (Exception)
                {
                    status = 0;
                    starttype = ServiceStartMode.Disabled;
                }

                if (nextStatus != 0)
                {
                    try
                    {
                        controller.WaitForStatus(nextStatus, TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        nextStatus = 0;
                        prevStatus = 0;
                        logger.LogError("{0} set to {1}", name, ex.Message);
                    }
                }

                if (prevStatus != (int)status)
                {
                    helper.UpdateService(status, starttype);
                    logger.LogInformation("Controller Service status has changed to: {0}", status.ToString());
                }

                prevStatus = (int)status;
            }
        }

        public void CreateService(string path)
        {
            process.StartInfo.Arguments = $"create {name} binpath= \"{path}\" start= \"auto\" DisplayName= \"{display}\"";
            process.Start();
            process.WaitForExit();

            process.StartInfo.Arguments = $"description {name} \"{description}\"";
            process.Start();
            process.WaitForExit();

            nextStatus = ServiceControllerStatus.Stopped;
        }

        public void DeleteService()
        {
            process.StartInfo.Arguments = $"delete {name}";
            process.Start();
            process.WaitForExit();

            nextStatus = 0;
        }

        public void StartService()
        {
            controller.Start();
            nextStatus = ServiceControllerStatus.Running;
        }

        public void StopService()
        {
            controller.Stop();
            nextStatus = ServiceControllerStatus.Stopped;
        }

        internal void SetStartType(ServiceStartMode mode)
        {
            ServiceHelper.ChangeStartMode(controller, mode);
        }
    }
}
