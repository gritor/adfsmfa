﻿//******************************************************************************************************************************************************************************************//
// Copyright (c) 2019 Neos-Sdi (http://www.neos-sdi.com)                                                                                                                                    //                        
//                                                                                                                                                                                          //
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"),                                       //
// to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software,   //
// and to permit persons to whom the Software is furnished to do so, subject to the following conditions:                                                                                   //
//                                                                                                                                                                                          //
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.                                                           //
//                                                                                                                                                                                          //
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,                                      //
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,                            //
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.                               //
//                                                                                                                                                                                          //
// https://adfsmfa.codeplex.com                                                                                                                                                             //
// https://github.com/neos-sdi/adfsmfa                                                                                                                                                      //
//                                                                                                                                                                                          //
//******************************************************************************************************************************************************************************************//
using Microsoft.Win32;
using Neos.IdentityServer.MultiFactor.Administration.Resources;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Data.SqlClient;
using System.Security.Cryptography;
using Neos.IdentityServer.MultiFactor.Data;

namespace Neos.IdentityServer.MultiFactor.Administration
{
    #region enums
    /// <summary>
    /// ServiceOperationStatus enum
    /// </summary>
    public enum ServiceOperationStatus
    {
        OperationUnknown,
        OperationPending,
        OperationRunning,
        OperationStopped,
        OperationInError
    }

    /// <summary>
    /// ServiceOperationStatus enum
    /// </summary>
    public enum ConfigOperationStatus
    {
        ConfigUnknown,
        ConfigLoaded,
        ConfigIsDirty,
        ConfigSaved,
        ConfigStopped,
        ConfigInError
    }
    #endregion

    #region ADFSServiceManager
    /// <summary>
    /// ADFSServiceManager Class
    /// </summary>
    public class ADFSServiceManager
    {
        public delegate void ADFSServiceStatus(ADFSServiceManager mgr, ServiceOperationStatus status, string servername, Exception Ex = null);
        public delegate void ADFSConfigStatus(ADFSServiceManager mgr, ConfigOperationStatus status, Exception ex = null);
        public event ADFSServiceStatus ServiceStatusChanged;
        public event ADFSConfigStatus ConfigurationStatusChanged;
        private bool _isprimaryserver = false;
        private bool _isprimaryserverread = false;

        #region Constructors
        /// <summary>
        /// ADFSServiceManager constructor
        /// </summary>
        public ADFSServiceManager()
        {
            ServiceStatusChanged += DefaultServiceStatusChanged;
            ConfigurationStatusChanged += DefaultConfigurationStatusChanged;
            if (IsRunning())
                ServicesStatus = ServiceOperationStatus.OperationRunning;
            else
                ServicesStatus = ServiceOperationStatus.OperationStopped;
            this.ServiceStatusChanged(this, ServicesStatus, "");
            ConfigurationStatus = ConfigOperationStatus.ConfigUnknown;
            this.ConfigurationStatusChanged(this, ConfigurationStatus);
        }

        /// <summary>
        /// Initialize method implementation
        /// </summary>
        public void Initialize(string mailslothost = "MGT", bool dontthrow = false)
        {
            try
            {
                ServiceController ADFSController = new ServiceController("mfanotifhub");
                try
                {
                    if ((ADFSController.Status != ServiceControllerStatus.Running) && (ADFSController.Status != ServiceControllerStatus.StartPending))
                    {
                        ADFSController.Start();
                        ADFSController.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan(0, 0, 30));
                    }
                }
                catch (Exception e)
                {
                    if (!dontthrow)
                       throw e;
                }
            }
            catch (Exception e)
            {
                if (!dontthrow)
                   throw e;
            }
        }

        public void OnServiceStatusChanged(ServiceOperationStatus status, string servername, Exception Ex = null)
        {
            this.ServiceStatusChanged(this, status, servername, Ex);
        }

        public void OnConfigurationStatusChanged(ConfigOperationStatus status, Exception Ex = null)
        {
            this.ConfigurationStatusChanged(this, status, Ex);
        }

        /// <summary>
        /// MailSlotMessageArrived method implmentation
        /// </summary>
        private void MailSlotMessageArrived(MailSlotServer maislotserver, MailSlotMessage message)
        {
            if (message.Operation == (byte)NotificationsKind.ServiceStatusRunning)
            {
                ServicesStatus = ServiceOperationStatus.OperationRunning;
                this.ServiceStatusChanged(this, ServicesStatus, message.Text);
            }
            else if (message.Operation == (byte)NotificationsKind.ServiceStatusStopped)
            {
                ServicesStatus = ServiceOperationStatus.OperationStopped;
                this.ServiceStatusChanged(this, ServicesStatus, message.Text);
            }
            else if (message.Operation == (byte)NotificationsKind.ServiceStatusPending)
            {
                ServicesStatus = ServiceOperationStatus.OperationPending;
                this.ServiceStatusChanged(this, ServicesStatus, message.Text);
            }
            else if (message.Operation == (byte)NotificationsKind.ServiceStatusInError)
            {
                ServicesStatus = ServiceOperationStatus.OperationInError;
                this.ServiceStatusChanged(this, ServicesStatus, message.Text);
            }
        }
        #endregion

        #region Utility Methods and events
        /// <summary>
        /// DefaultServiceStatusChanged method implementation
        /// </summary>
        private void DefaultServiceStatusChanged(ADFSServiceManager mgr, ServiceOperationStatus status, string servername, Exception Ex = null)
        {
            mgr.ServicesStatus = status;
        }

        /// <summary>
        /// DefaultConfigurationStatusChanged method implmentation 
        /// </summary>
        private void DefaultConfigurationStatusChanged(ADFSServiceManager mgr, ConfigOperationStatus status, Exception Ex = null)
        {
            mgr.ConfigurationStatus = status;
        }

        /// <summary>
        /// RefreshServiceStatus method implementation
        /// </summary>
        public void RefreshServiceStatus()
        {
            this.ServiceStatusChanged(this, ServicesStatus, "");
        }

        /// <summary>
        /// RefreshServiceStatus method implementation
        /// </summary>
        public void RefreshConfigurationStatus()
        {
            this.ConfigurationStatusChanged(this, ConfigurationStatus);
        }

        /// <summary>
        /// EnsureLocalService method implementation
        /// </summary>
        public void EnsureLocalService()
        {
            if (!IsADFSServer())
                throw new Exception(SErrors.ErrorADFSPlatformNotSupported); 
            if (!IsRunning())
                StartService();
        }

        /// <summary>
        /// EnsureConfiguration method implementation
        /// </summary>
        private void EnsureConfiguration(PSHost Host)
        {
            if (Config == null)
            {
                EnsureLocalService();
                Config = ReadConfiguration(Host);
                if (!IsFarmConfigured())
                    throw new Exception(SErrors.ErrorMFAFarmNotInitialized);
            }
        }

        /// <summary>
        /// EnsureLocalConfiguration method implementation
        /// </summary>
        public void EnsureLocalConfiguration(PSHost Host = null)
        {
            EnsureConfiguration(Host);
            if (Config == null)
                throw new Exception(SErrors.ErrorLoadingMFAConfiguration);
            return;
        }

        /// <summary>
        /// SetDirty method implmentayion
        /// </summary>
        public void SetDirty(bool value)
        {
            EnsureConfiguration(null);
            Config.IsDirty = value;
            if (value)
                this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigIsDirty);
        }
        #endregion

        #region Properties
        /// <summary>
        /// ADFSServers property
        /// </summary>
        public ADFSFarmHost ADFSFarm
        {
            get
            {
              //  EnsureConfiguration();
                if (Config != null)
                    return Config.Hosts.ADFSFarm;
                else
                    return null;
            }
        }

        /// <summary>
        /// Config property implementation
        /// </summary>
        public MFAConfig Config { get; internal set; } = null;

        /// <summary>
        /// ServicesStatus property
        /// </summary>
        public ServiceOperationStatus ServicesStatus { get; set; }

        /// <summary>
        /// ConfigurationStatus property
        /// </summary>
        public ConfigOperationStatus ConfigurationStatus { get; set; }
        #endregion

        #region ADFS Services
        /// <summary>
        /// IsFarmConfigured method implementation
        /// </summary>
        public bool IsFarmConfigured()
        {
            if (ADFSFarm==null)
                return false;
            else
                return ADFSFarm.IsInitialized;
        }

        /// <summary>
        /// IsRunning method iplementation
        /// </summary>
        public bool IsADFSServer(string servername = "local")
        {
            ServiceController ADFSController = null;
            try
            {
                if (servername.ToLowerInvariant().Equals("local"))
                    ADFSController = new ServiceController("adfssrv");
                else
                    ADFSController = new ServiceController("adfssrv", servername);
                ADFSController.Refresh();
                ServiceControllerStatus st = ADFSController.Status;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (ADFSController != null)
                    ADFSController.Close();
            }
        }

        /// <summary>
        /// IsRunning method iplementation
        /// </summary>
        public bool IsRunning(string servername = "local")
        {
            ServiceController ADFSController = null;
            try
            {
                if (servername.ToLowerInvariant().Equals("local"))
                    ADFSController = new ServiceController("adfssrv");
                else
                    ADFSController = new ServiceController("adfssrv", servername);
                ADFSController.Refresh();
                return ADFSController.Status == ServiceControllerStatus.Running;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (ADFSController != null)
                    ADFSController.Close();
            }
        }

        /// <summary>
        /// StartService method implementation
        /// </summary>
        public bool StartService(string servername = null)
        {
            if (servername != null)
                return InternalStartService(servername);
            else
                return InternalStartService();
        }

        /// <summary>
        /// StartMFAService method implementation
        /// </summary>
        public bool StartMFAService(string servername = null)
        {
            if (servername != null)
                return InternalStartMFAService(servername);
            else
                return InternalStartMFAService();
        }

        /// <summary>
        /// StopService method implementation
        /// </summary>
        public bool StopService(string servername = null)
        {
            if (servername != null)
               return InternalStopService(servername);
            else
                return InternalStopService();
        }

        /// <summary>
        /// StopMFAService method implementation
        /// </summary>
        public bool StopMFAService(string servername = null)
        {
            if (servername != null)
                return InternalStopMFAService(servername);
            else
                return InternalStopMFAService();
        }

        /// <summary>
        /// RestartFarm method implmentation
        /// </summary>
        public void RestartFarm(PSHost Host = null)
        {
            if (Host != null)
                Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Stopping ADFS Farm...");
            if (ADFSFarm != null)
            {
                foreach (ADFSServerHost srv in ADFSFarm.Servers)
                {
                    if (Host != null)
                        Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Stopping " + srv.FQDN.ToLower() + " ...");
                    StopService(srv.FQDN);
                    if (Host != null)
                        Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Starting " + srv.FQDN.ToLower() + " ...");
                    StartService(srv.FQDN);
                }
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "ADFS Farm Started");
            }
            else
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "ADFS Farm Not Starte !");

        }

        /// <summary>
        /// RestartFarm method implmentation
        /// </summary>
        public void RestartAllFarm(PSHost Host = null)
        {
            if (Host != null)
                Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Stopping ADFS Farm...");
            if (ADFSFarm != null)
            {
                foreach (ADFSServerHost srv in ADFSFarm.Servers)
                {
                    if (Host != null)
                        Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Stopping " + srv.FQDN.ToLower() + " ...");
                    StopMFAService(srv.FQDN);
                    if (Host != null)
                        Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Starting " + srv.FQDN.ToLower() + " ...");
                    StartMFAService(srv.FQDN);
                }
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "ADFS Farm Started");
            }
            else
                if (Host != null)
                Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "ADFS Farm Not Starte !");

        }

        /// RestartFarm method implmentation
        /// </summary>
        public bool RestartServer(PSHost Host = null, string servername = "local")
        {
            bool result = false;
            if (servername.ToLower().Equals("local"))
            {
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Stopping  ...");
                StopService(servername);
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Starting  ...");
                StartService(servername);
                result = true;
            }
            else
            {
                foreach (ADFSServerHost srv in ADFSFarm.Servers)
                {
                    if (srv.FQDN.ToLower().Equals(servername.ToLower()))
                    {
                        if (Host != null)
                            Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Stopping " + srv.FQDN.ToLower() + " ...");
                        StopService(srv.FQDN);
                        if (Host != null)
                            Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : " + "Starting " + srv.FQDN.ToLower() + " ...");
                        StartService(srv.FQDN);
                        result = true;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// InternalStartService method implementation
        /// </summary>
        private bool InternalStartService(string servername = "local")
        {
            ServiceController ADFSController = null;
            try
            {
                this.ServiceStatusChanged(this, ServicesStatus, servername);
                if (servername.ToLowerInvariant().Equals("local"))
                    ADFSController = new ServiceController("adfssrv");
                else
                    ADFSController = new ServiceController("adfssrv", servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusPending);
                }
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationPending, servername);
                if ((ADFSController.Status != ServiceControllerStatus.Running) && (ADFSController.Status != ServiceControllerStatus.StartPending))
                {
                    ADFSController.Start();
                    ADFSController.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan(0, 1, 0));
                }
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationRunning, servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusRunning);
                }
                return true;
            }
            catch (Exception)
            {
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationInError, servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusInError);
                }
                return false;
            }
            finally
            {
                ADFSController.Close();
            }
        }

        /// <summary>
        /// InternalStartMFAService method implementation
        /// </summary>
        private bool InternalStartMFAService(string servername = "local")
        {
            ServiceController SVCController = null;
            try
            {
                this.ServiceStatusChanged(this, ServicesStatus, servername);
                if (servername.ToLowerInvariant().Equals("local"))
                    SVCController = new ServiceController("mfanotifhub");
                else
                    SVCController = new ServiceController("mfanotifhub", servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusPending);
                }
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationPending, servername);
                if ((SVCController.Status != ServiceControllerStatus.Running) && (SVCController.Status != ServiceControllerStatus.StartPending))
                {
                    SVCController.Start();
                    SVCController.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan(0, 1, 0));
                }
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationRunning, servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusRunning);
                }
                return true;
            }
            catch (Exception)
            {
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationInError, servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusInError);
                }
                return false;
            }
            finally
            {
                SVCController.Close();
            }
        }

        /// <summary>
        /// internalStopService method implementation
        /// </summary>
        private bool InternalStopService(string servername = "local")
        {
            ServiceController ADFSController = null;
            try
            {
                this.ServiceStatusChanged(this, ServicesStatus, servername);
                if (servername.ToLowerInvariant().Equals("local"))
                    ADFSController = new ServiceController("adfssrv");
                else
                    ADFSController = new ServiceController("adfssrv", servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusPending);
                }
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationPending, servername);
                if ((ADFSController.Status != ServiceControllerStatus.Stopped) && (ADFSController.Status != ServiceControllerStatus.StopPending))
                {
                    ADFSController.Stop();
                    ADFSController.WaitForStatus(ServiceControllerStatus.Stopped, new TimeSpan(0, 1, 0));
                }
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationStopped, servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusRunning);
                }
                return true;
            }
            catch (Exception)
            {
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationInError, servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusInError);
                }
                return false;
            }
            finally
            {
                ADFSController.Close();
            }
        }

        /// <summary>
        /// internalStopMFAService method implementation
        /// </summary>
        private bool InternalStopMFAService(string servername = "local")
        {
            ServiceController SVCController = null;
            try
            {
                this.ServiceStatusChanged(this, ServicesStatus, servername);
                if (servername.ToLowerInvariant().Equals("local"))
                    SVCController = new ServiceController("mfanotifhub");
                else
                    SVCController = new ServiceController("mfanotifhub", servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusPending);
                }
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationPending, servername);
                if ((SVCController.Status != ServiceControllerStatus.Stopped) && (SVCController.Status != ServiceControllerStatus.StopPending))
                {
                    SVCController.Stop();
                    SVCController.WaitForStatus(ServiceControllerStatus.Stopped, new TimeSpan(0, 1, 0));
                }
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationStopped, servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusRunning);
                }
                return true;
            }
            catch (Exception)
            {
                this.ServiceStatusChanged(this, ServiceOperationStatus.OperationInError, servername);
                using (MailSlotClient mailslot = new MailSlotClient("MGT"))
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceStatusInError);
                }
                return false;
            }
            finally
            {
                SVCController.Close();
            }
        }
        #endregion

        #region MFA Configuration Store
        /// <summary>
        /// ReadConfiguration method implementation
        /// </summary>
        public MFAConfig ReadConfiguration(PSHost Host = null)
        {
            this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigIsDirty);
            try
            {
                EnsureLocalService();
                Config = CFGUtilities.ReadConfiguration(Host);
                Config.IsDirty = false;
#if hardcheck
                if (this.IsMFAProviderEnabled(Host))
#else
                if (Config != null)
#endif
                    this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigLoaded);
                else
                    this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigStopped);
            }
            catch (CmdletInvocationException cm)
            {
                this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigInError, cm);
                throw new CmdletInvocationException(SErrors.ErrorMFAFarmNotInitialized, cm);
            }
            catch (Exception ex)
            {
                this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigInError, ex);
            }
            return Config;
        }

        /// <summary>
        /// WriteConfiguration method implmentation
        /// </summary>
        public void WriteConfiguration(PSHost Host = null)
        {
            EnsureConfiguration(Host);
            this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigIsDirty);
            try
            {
                EnsureLocalService();
                Config.IsDirty = false;
                CFGUtilities.WriteConfiguration(Host, Config);
                this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigSaved);
                using (MailSlotClient mailslot = new MailSlotClient())
                {
                    mailslot.Text = Environment.MachineName;
                    mailslot.SendNotification(NotificationsKind.ConfigurationReload);
                }
            }
            catch (Exception ex)
            {
                this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigInError, ex);
            }
        }

        /// <summary>
        /// internalRegisterConfiguration method implementation
        /// </summary>
        private void InternalRegisterConfiguration(PSHost Host)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            string pth = Path.GetTempPath() + Path.GetRandomFileName();
            try
            {
                FileStream stm = new FileStream(pth, FileMode.CreateNew, FileAccess.ReadWrite);
                XmlConfigSerializer xmlserializer = new XmlConfigSerializer(typeof(MFAConfig));
                stm.Position = 0;
                using (StreamReader reader = new StreamReader(stm))
                {
                    xmlserializer.Serialize(stm, Config);
                }
                try
                {
                    RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                    SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                    SPPowerShell = PowerShell.Create();
                    SPPowerShell.Runspace = SPRunSpace;
                    SPRunSpace.Open();

                    Pipeline pipeline = SPRunSpace.CreatePipeline();
                    Command exportcmd = new Command("Register-AdfsAuthenticationProvider", false);
                    CommandParameter NParam = new CommandParameter("Name", "MultiFactorAuthenticationProvider");
                    exportcmd.Parameters.Add(NParam);
                    CommandParameter TParam = new CommandParameter("TypeName", "Neos.IdentityServer.MultiFactor.AuthenticationProvider, Neos.IdentityServer.MultiFactor, Version=2.5.0.0, Culture=neutral, PublicKeyToken=175aa5ee756d2aa2");
                    exportcmd.Parameters.Add(TParam);
                    CommandParameter PParam = new CommandParameter("ConfigurationFilePath", pth);
                    exportcmd.Parameters.Add(PParam);
                    pipeline.Commands.Add(exportcmd);
                    Collection<PSObject> PSOutput = pipeline.Invoke();
                    if (Host != null)
                        Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : Registered");
                }
                finally
                {
                    if (SPRunSpace != null)
                        SPRunSpace.Close();
                }
            }
            finally
            {
                if (File.Exists(pth))
                    File.Delete(pth);
            }
            return;
        }

        /// <summary>
        /// internalUnRegisterConfiguration method implementation
        /// </summary>
        private void InternalUnRegisterConfiguration(PSHost Host)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("UnRegister-AdfsAuthenticationProvider", false);
                CommandParameter NParam = new CommandParameter("Name", "MultiFactorAuthenticationProvider");
                exportcmd.Parameters.Add(NParam);
                CommandParameter CParam = new CommandParameter("Confirm", false);
                exportcmd.Parameters.Add(CParam);

                pipeline.Commands.Add(exportcmd);
                Collection<PSObject> PSOutput = pipeline.Invoke();
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : Removed");
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
            return ;
        }

        /// <summary>
        /// internalExportConfiguration method implmentation
        /// </summary>
        private void InternalExportConfiguration(PSHost Host, string backupfile)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            string pth = string.Empty;
            if (string.IsNullOrEmpty(backupfile))
                pth = Path.GetTempPath() + "adfsmfa_backup_" + DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm") + ".xml";
            else
                pth = backupfile;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("Export-AdfsAuthenticationProviderConfigurationData", false);
                CommandParameter NParam = new CommandParameter("Name", "MultiFactorAuthenticationProvider");
                exportcmd.Parameters.Add(NParam);
                CommandParameter PParam = new CommandParameter("FilePath", pth);
                exportcmd.Parameters.Add(PParam);
                pipeline.Commands.Add(exportcmd);
                Collection<PSObject> PSOutput = pipeline.Invoke();
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA Configuration saved to => "+pth);
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
        }

        /// <summary>
        /// internalImportConfiguration method implmentation
        /// </summary>
        private void InternalImportConfiguration(PSHost Host, string importfile)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("Import-AdfsAuthenticationProviderConfigurationData", false);
                CommandParameter NParam = new CommandParameter("Name", "MultiFactorAuthenticationProvider");
                exportcmd.Parameters.Add(NParam);
                CommandParameter PParam = new CommandParameter("FilePath", importfile);
                exportcmd.Parameters.Add(PParam);
                pipeline.Commands.Add(exportcmd);
                Collection<PSObject> PSOutput = pipeline.Invoke();
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA Configuration saved to => " + importfile);
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
        }

        /// <summary>
        /// internalActivateConfiguration method implementation
        /// </summary>
        private void InternalActivateConfiguration(PSHost Host)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            bool found = false;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("(Get-AdfsGlobalAuthenticationPolicy).AdditionalAuthenticationProvider", true);
                pipeline.Commands.Add(exportcmd);
                Collection<PSObject> PSOutput = pipeline.Invoke();
                List<string> lst = new List<string>();
                try
                {
                    foreach (var result in PSOutput)
                    {
                        if (result.BaseObject.ToString().ToLower().Equals("multifactorauthenticationprovider"))
                        {
                            found = true;
                            break;
                        }
                        else
                        {
                            lst.Add(result.BaseObject.ToString());
                        }
                    }
                }
                catch (Exception)
                {
                    found = false;
                }
                if (!found)
                {
                    lst.Add("MultiFactorAuthenticationProvider");
                    Pipeline pipeline2 = SPRunSpace.CreatePipeline();
                    Command exportcmd2 = new Command("Set-AdfsGlobalAuthenticationPolicy", false);
                    pipeline2.Commands.Add(exportcmd2);
                    CommandParameter NParam = new CommandParameter("AdditionalAuthenticationProvider", lst);
                    exportcmd2.Parameters.Add(NParam);
                    Collection<PSObject> PSOutput2 = pipeline2.Invoke();
                    if (Host != null)
                        Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : Enabled");
                }
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
        }

        /// <summary>
        /// internalDeActivateConfiguration method implementation
        /// </summary>
        private void InternalDeActivateConfiguration(PSHost Host)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            bool found = false;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("(Get-AdfsGlobalAuthenticationPolicy).AdditionalAuthenticationProvider", true);
                pipeline.Commands.Add(exportcmd);
                Collection<PSObject> PSOutput = pipeline.Invoke();
                List<string> lst = new List<string>();
                try
                {
                    foreach (var result in PSOutput)
                    {
                        if (result.BaseObject.ToString().ToLower().Equals("multifactorauthenticationprovider"))
                        {
                            found = true;
                            break;
                        }
                        else
                        {
                            lst.Add(result.BaseObject.ToString());
                        }
                    }
                }
                catch (Exception)
                {
                    found = false;
                }
                if (found)
                {
                    if (lst.Count == 0)
                        lst = null;
                    Pipeline pipeline2 = SPRunSpace.CreatePipeline();
                    Command exportcmd2 = new Command("Set-AdfsGlobalAuthenticationPolicy", false);
                    pipeline2.Commands.Add(exportcmd2);
                    CommandParameter NParam = new CommandParameter("AdditionalAuthenticationProvider", lst);
                    exportcmd2.Parameters.Add(NParam);
                    Collection<PSObject> PSOutput2 = pipeline2.Invoke();
                    if (Host != null)
                        Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA System : Disabled");
                }
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
        }

        /// <summary>
        /// internalIsConfigurationActive method implementation
        /// </summary>
        private bool InternalIsConfigurationActive(PSHost Host)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            bool found = false;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("(Get-AdfsGlobalAuthenticationPolicy).AdditionalAuthenticationProvider", true);
                pipeline.Commands.Add(exportcmd);
                Collection<PSObject> PSOutput = pipeline.Invoke();
                try
                {
                    foreach (var result in PSOutput)
                    {
                        if (result.BaseObject.ToString().ToLower().Equals("multifactorauthenticationprovider"))
                        {
                            found = true;
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    found = false;
                }
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
            return found;
        }

        /// <summary>
        /// IsPrimaryServer method implementation
        /// </summary>
        internal bool IsPrimaryServer()
        {
            if (!_isprimaryserverread)
            {
                Runspace SPRunSpace = null;
                PowerShell SPPowerShell = null;
                try
                {
                    _isprimaryserverread = true;
                    RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                    SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                    SPPowerShell = PowerShell.Create();
                    SPPowerShell.Runspace = SPRunSpace;
                    SPRunSpace.Open();

                    Pipeline pipeline = SPRunSpace.CreatePipeline();
                    Command exportcmd = new Command("(Get-AdfsSyncProperties).Role", true);
                    pipeline.Commands.Add(exportcmd);
                    Collection<PSObject> PSOutput = pipeline.Invoke();
                    foreach (var result in PSOutput)
                    {
                        if (!result.BaseObject.ToString().ToLower().Equals("primarycomputer"))
                        {
                            _isprimaryserver = false;
                            return false;
                        }
                    }
                    _isprimaryserver = true;
                    return true;
                }
                finally
                {
                    if (SPRunSpace != null)
                        SPRunSpace.Close();
                }
            }
            else
                return _isprimaryserver;
        }

        /// <summary>
        /// SetADFSTheme method implementation
        /// </summary>
        internal void SetADFSTheme(PSHost pSHost, string themename, bool paginated, bool supports2019)
        {
            InternalSetADFSTheme(pSHost, themename, paginated, supports2019);
        }

        /// <summary>
        /// internalSetADFSTheme method implementation
        /// </summary>
        private void InternalSetADFSTheme(PSHost Host, string themename, bool paginated, bool supports2019)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                if (supports2019)
                {
                    Pipeline pipeline = SPRunSpace.CreatePipeline();
                    Command policycmd = new Command("Set-AdfsGlobalAuthenticationPolicy", false);
                    CommandParameter PParam = new CommandParameter("EnablePaginatedAuthenticationPages", paginated);
                    policycmd.Parameters.Add(PParam);
                    CommandParameter CParam = new CommandParameter("Force", true);
                    policycmd.Parameters.Add(CParam);
                    pipeline.Commands.Add(policycmd);

                    Collection<PSObject> PSOutput = pipeline.Invoke();
                    if (Host != null)
                        Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " ADFS MFA Pagination : Changed");
                }

                Pipeline pipeline2 = SPRunSpace.CreatePipeline();
                Command themecmd = new Command("Set-AdfsWebConfig", false);
                CommandParameter NParam = new CommandParameter("ActiveThemeName", themename);
                themecmd.Parameters.Add(NParam);
                pipeline2.Commands.Add(themecmd);

                Collection<PSObject> PSOutput2 = pipeline2.Invoke();
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " ADFS MFA Theme : Changed");
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
            return;
        }


        /// <summary>
        /// RegisterNewRSACertificate method implmentation
        /// </summary>
        public string RegisterNewRSACertificate(PSHost Host = null, int years = 5, bool restart = true)
        {
            if (Config.KeysConfig.KeyFormat == SecretKeyFormat.RSA)
            {
                Config.KeysConfig.CertificateThumbprint = InternalRegisterNewRSACertificate(Host, years);
                Config.IsDirty = true;
                CFGUtilities.WriteConfiguration(Host, Config);
                if (restart)
                    RestartFarm(Host);
                return Config.KeysConfig.CertificateThumbprint;
            }
            else
            {
                if (Host != null)
                    Host.UI.WriteWarningLine(DateTime.Now.ToLongTimeString() + " MFA System : Configuration is not RSA ! no action taken !");
                else
                    throw new Exception("MFA System : Configuration is not RSA ! no action taken !");
                return "";
            }
        }

        /// <summary>
        /// RegisterNewRSACertificate method implmentation
        /// </summary>
        public string RegisterNewSQLCertificate(PSHost Host = null, int years = 5, string keyname = "adfsmfa")
        {
            Config.Hosts.SQLServerHost.ThumbPrint = InternalRegisterNewSQLCertificate(Host, years, keyname);
            Config.IsDirty = true;
            CFGUtilities.WriteConfiguration(Host, Config);
            return Config.Hosts.SQLServerHost.ThumbPrint;
        }

        /// <summary>
        /// CheckCertificate metnod implementation
        /// </summary>
        internal bool CheckCertificate(string thumbprint, StoreLocation location = StoreLocation.LocalMachine)
        {
            X509Certificate2 cert = Certs.GetCertificate(thumbprint, location);
            if (cert!=null)
                cert.Reset();
            return (cert != null);
        }

        /// <summary>
        /// internalRegisterNewRSACertificate method implementation
        /// </summary>
        private string InternalRegisterNewRSACertificate(PSHost Host, int years)
        {
            X509Certificate2 cert = Certs.CreateRSACertificate("MFA RSA Keys", years);
            if (cert != null)
            {
                string thumbprint = cert.Thumbprint;
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA Certificate \"" + thumbprint + "\" Created for using with RSA keys");
                cert.Reset();
                return thumbprint;
            }
            else
                return "";
        }

        /// <summary>
        /// internalRegisterNewSQLCertificate method implementation
        /// </summary>
        private string InternalRegisterNewSQLCertificate(PSHost Host, int years, string keyname)
        {
            X509Certificate2 cert = Certs.CreateRSACertificateForSQLEncryption("MFA SQL Key : " + keyname, years);
            if (cert != null)
            {
                string thumbprint = cert.Thumbprint;
                if (Host != null)
                    Host.UI.WriteVerboseLine(DateTime.Now.ToLongTimeString() + " MFA Certificate \"" + thumbprint + "\" Created for using with SQL keys");
                cert.Reset();
                return thumbprint;
            }
            else
                return "";
        }

        /// <summary>
        /// RegisterMFAProvider method implmentation
        /// </summary>
        public bool RegisterMFAProvider(PSHost host)
        {
            if (Config != null)
                return false;

            EnsureLocalService();
            Config = new MFAConfig(true);
            if (Config != null)
            {
                InternalRegisterConfiguration(host);
                InternalActivateConfiguration(host);
                InitFarmConfiguration(host);
                WriteConfiguration(host);
                using (MailSlotClient mailslot = new MailSlotClient())
                {
                    mailslot.Text = Environment.MachineName;
                    mailslot.SendNotification(NotificationsKind.ConfigurationCreated);
                }
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// UnRegisterMFAProvider method implmentation
        /// </summary>
        public bool UnRegisterMFAProvider(PSHost Host)
        {
            if (Config == null)
            {
                EnsureConfiguration(Host);
                Config = ReadConfiguration(Host);
            }
            if (Config == null)
                return false;
            using (MailSlotClient mailslot = new MailSlotClient())
            {
                mailslot.Text = Environment.MachineName;
                mailslot.SendNotification(NotificationsKind.ConfigurationDeleted);
            }
            InternalDeActivateConfiguration(Host);
            InternalUnRegisterConfiguration(Host);
            File.Delete(CFGUtilities.configcachedir);
            Config = null;
            return true;
        }

        /// <summary>
        /// ImportMFAProviderConfiguration method implementation
        /// </summary>
        public bool ImportMFAProviderConfiguration(PSHost Host, bool activate, bool restartfarm, string importfile)
        {
            if (Config == null)
            {
                EnsureLocalService();
                Config = ReadConfiguration(Host);
            }
            this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigIsDirty);
            try
            {
                InternalImportConfiguration(Host, importfile);
                Config = CFGUtilities.ReadConfigurationFromDatabase(Host);
                Config.IsDirty = false;
                CFGUtilities.WriteConfigurationToCache(Config);
                using (MailSlotClient mailslot = new MailSlotClient())
                {
                    mailslot.Text = Environment.MachineName;
                    mailslot.SendNotification(NotificationsKind.ConfigurationReload);
                }
                this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigSaved);
                if (KeysManager.IsLoaded)
                {
                    if (activate)
                        InternalActivateConfiguration(Host);
                    if (restartfarm)
                        RestartFarm(Host);
                }
                else
                    throw new NotSupportedException("Invalid key manager !");
            }
            catch (NotSupportedException ex)
            {
                this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigInError, ex);
                return false;
            }
            catch (Exception ex)
            {
                this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigInError, ex);
                throw ex;
            }
            return true;
        }

        /// <summary>
        /// ExportMFAProviderConfiguration method implementation
        /// </summary>
        public void ExportMFAProviderConfiguration(PSHost Host, string exportfilepath)
        {
            if (Config == null)
            {
                EnsureConfiguration(Host);
                Config = ReadConfiguration(Host);
            }
            InternalExportConfiguration(Host, exportfilepath);
        }

        /// <summary>
        /// EnableMFAProvider method implmentation
        /// </summary>
        public void EnableMFAProvider(PSHost Host)
        {
            if (Config == null)
            {
                EnsureLocalService();
                Config = ReadConfiguration(Host);
                if (Config == null)
                    Config = new MFAConfig(true);
            }
            if (!Config.Hosts.ADFSFarm.IsInitialized)
                throw new Exception(SErrors.ErrorMFAFarmNotInitialized);
            InternalActivateConfiguration(Host);
            using (MailSlotClient mailslot = new MailSlotClient())
            {
                mailslot.Text = Environment.MachineName;
                mailslot.SendNotification(NotificationsKind.ConfigurationReload);
            }
        }

        /// <summary>
        /// DisableMFAProvider method implmentation
        /// </summary>
        public void DisableMFAProvider(PSHost Host)
        {
            if (Config == null)
            {
                EnsureConfiguration(Host);
                Config = ReadConfiguration(Host);
            }
            InternalDeActivateConfiguration(Host);
            this.ConfigurationStatusChanged(this, ConfigOperationStatus.ConfigStopped);
            using (MailSlotClient mailslot = new MailSlotClient())
            {
                mailslot.Text = Environment.MachineName;
                mailslot.SendNotification(NotificationsKind.ConfigurationReload);
            }
        }

        /// <summary>
        /// IsMFAProviderEnabled method implmentation
        /// </summary>
        public bool IsMFAProviderEnabled(PSHost Host)
        {
            if (Config == null)
            {
                EnsureConfiguration(Host);
                Config = ReadConfiguration(Host);
            }
            return InternalIsConfigurationActive(Host);
        }
        #endregion

        /// <summary>
        /// RegisterADFSComputer method implementation
        /// </summary>        
        public void RegisterADFSComputer(PSHost Host, string servername)
        {
            EnsureConfiguration(Host);
            try
            {
                using (MailSlotClient mailslot = new MailSlotClient("NOT")) // Ask NotifyHub to register and broadcast notification for new server
                {
                    mailslot.Text = servername;
                    mailslot.SendNotification(NotificationsKind.ServiceServerInformation);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return;
        }

        /// <summary>
        /// UnRegisterADFSComputer method implementation
        /// </summary>
        public void UnRegisterADFSComputer(PSHost Host, string servername)
        {
            EnsureConfiguration(Host);
            try
            {
                string fqdn = Dns.GetHostEntry(servername).HostName;
                ADFSFarm.Servers.RemoveAll(c => c.FQDN.ToLower() == fqdn.ToLower());
                SetDirty(true);
                WriteConfiguration(Host); // Save, Notification and Sync
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return;
        }

        #region Farm Configuration
        /// <summary>
        /// InitFarmNodesConfiguration method implementation
        /// </summary>
        private ADFSServerHost InitFarmConfiguration(PSHost Host)
        {
            ADFSServerHost result = null;
            try
            {
                RegistryVersion reg = new RegistryVersion();
                InitFarmProperties(Host);
                if (reg.IsWindows2019)
                {
                    result = InitServerNodeConfiguration2019(Host, reg);
                }
                else if (reg.IsWindows2016)
                {
                    result = InitServerNodeConfiguration2016(Host, reg);
                }
                else if (reg.IsWindows2012R2)
                {
                    result = InitServerNodeConfiguration2012(Host, reg);
                }
                ADFSFarm.IsInitialized = true;
                SetDirty(true);
            }
            catch (Exception ex)
            {
                ADFSFarm.IsInitialized = false;
                throw ex;
            }
            return result;
        }

        /// <summary>
        /// InitFarmProperties method implementation
        /// </summary>
        private void InitFarmProperties(PSHost Host)
        {
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);

                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline2 = SPRunSpace.CreatePipeline();
                Command exportcmd2 = new Command("(Get-AdfsFarmInformation).CurrentFarmBehavior", true);
                pipeline2.Commands.Add(exportcmd2);

                try
                {
                    Collection<PSObject> PSOutput2 = pipeline2.Invoke();
                    foreach (var result in PSOutput2)
                    {
                        ADFSFarm.CurrentFarmBehavior = Convert.ToInt32(result.BaseObject);
                        break;
                    }
                }
                catch (Exception)
                {
                    ADFSFarm.CurrentFarmBehavior = 1;
                }

                Pipeline pipeline3 = SPRunSpace.CreatePipeline();
                Command exportcmd3 = new Command("(Get-ADFSProperties).Identifier.OriginalString", true);
                pipeline3.Commands.Add(exportcmd3);

                Collection<PSObject> PSOutput3 = pipeline3.Invoke();
                foreach (var result in PSOutput3)
                {
                    ADFSFarm.FarmIdentifier = result.BaseObject.ToString();
                    break;
                }
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
            return;
        }
        #endregion

        #region Servers Configuration
        /// <summary>
        /// InitFarmNodeConfiguration method implementation
        /// </summary>        
        private ADFSServerHost InitServerNodeConfiguration(PSHost Host)
        {
            ADFSServerHost result = null;
            if (!ADFSFarm.IsInitialized)
                throw new Exception(SErrors.ErrorMFAFarmNotInitialized);
            EnsureConfiguration(Host);
            try
            {
                RegistryVersion reg = new RegistryVersion();
                if (reg.IsWindows2019)
                    result = InitServerNodeConfiguration2019(Host, reg);
                else if (reg.IsWindows2016)
                    result = InitServerNodeConfiguration2016(Host, reg);
                else if (reg.IsWindows2012R2)
                    result = InitServerNodeConfiguration2012(Host, reg);
                SetDirty(true);
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return result;
        }

        /// <summary>
        /// InitServerNodeConfiguration2012 method implementation
        /// </summary>
        private ADFSServerHost InitServerNodeConfiguration2012(PSHost Host, RegistryVersion reg)
        {

            string nodetype = string.Empty;
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);
                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("(Get-AdfsSyncProperties).Role", true);
                pipeline.Commands.Add(exportcmd);

                Collection<PSObject> PSOutput = pipeline.Invoke();
                foreach (var result in PSOutput)
                {
                    nodetype = result.BaseObject.ToString();
                    break;
                }
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }

            ADFSServerHost props = new ADFSServerHost
            {
                FQDN = Dns.GetHostEntry("LocalHost").HostName,
                BehaviorLevel = 1,
                HeartbeatTmeStamp = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, DateTime.Now.Minute, 0, DateTimeKind.Local),
                NodeType = nodetype,
                CurrentVersion = reg.CurrentVersion,
                CurrentBuild = reg.CurrentBuild,
                InstallationType = reg.InstallationType,
                ProductName = reg.ProductName,
                CurrentMajorVersionNumber = reg.CurrentMajorVersionNumber,
                CurrentMinorVersionNumber = reg.CurrentMinorVersionNumber
            };
            int i = ADFSFarm.Servers.FindIndex(c => c.FQDN.ToLower() == props.FQDN.ToLower());
            if (i < 0)
                ADFSFarm.Servers.Add(props);
            else
                ADFSFarm.Servers[i] = props;
            return props;
        }

        /// <summary>
        /// InitServerNodeConfiguration2016 method implementation
        /// </summary>
        private ADFSServerHost InitServerNodeConfiguration2016(PSHost Host, RegistryVersion reg)
        {
            ADFSServerHost xprops = null;
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);
                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("(Get-AdfsFarmInformation).FarmNodes", true);

                pipeline.Commands.Add(exportcmd);

                Collection<PSObject> PSOutput = pipeline.Invoke();
                foreach (var result in PSOutput)
                {
                    string fqdn = result.Members["FQDN"].Value.ToString();
                    ADFSServerHost props = new ADFSServerHost
                    {
                        FQDN = fqdn,
                        BehaviorLevel = Convert.ToInt32(result.Members["BehaviorLevel"].Value),
                        HeartbeatTmeStamp = Convert.ToDateTime(result.Members["HeartbeatTimeStamp"].Value),
                        NodeType = result.Members["NodeType"].Value.ToString(),
                        CurrentVersion = reg.CurrentVersion,
                        CurrentBuild = reg.CurrentBuild,
                        InstallationType = reg.InstallationType,
                        ProductName = reg.ProductName,
                        CurrentMajorVersionNumber = reg.CurrentMajorVersionNumber,
                        CurrentMinorVersionNumber = reg.CurrentMinorVersionNumber
                    };
                    int i = ADFSFarm.Servers.FindIndex(c => c.FQDN.ToLower() == props.FQDN.ToLower());
                    if (i<0)
                        ADFSFarm.Servers.Add(props);
                    else
                        ADFSFarm.Servers[i] = props;
                    xprops = props;
                }
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
            return xprops;
        }

        /// <summary>
        /// InitServerNodeConfiguration2019 method implementation
        /// </summary>
        private ADFSServerHost InitServerNodeConfiguration2019(PSHost Host, RegistryVersion reg)
        {
            ADFSServerHost xprops = null;
            Runspace SPRunSpace = null;
            PowerShell SPPowerShell = null;
            try
            {
                RunspaceConfiguration SPRunConfig = RunspaceConfiguration.Create();
                SPRunSpace = RunspaceFactory.CreateRunspace(SPRunConfig);
                SPPowerShell = PowerShell.Create();
                SPPowerShell.Runspace = SPRunSpace;
                SPRunSpace.Open();

                Pipeline pipeline = SPRunSpace.CreatePipeline();
                Command exportcmd = new Command("(Get-AdfsFarmInformation).FarmNodes", true);

                pipeline.Commands.Add(exportcmd);

                Collection<PSObject> PSOutput = pipeline.Invoke();
                foreach (var result in PSOutput)
                {
                    string fqdn = result.Members["FQDN"].Value.ToString();
                    ADFSServerHost props = new ADFSServerHost
                    {
                        FQDN = fqdn,
                        BehaviorLevel = Convert.ToInt32(result.Members["BehaviorLevel"].Value),
                        HeartbeatTmeStamp = Convert.ToDateTime(result.Members["HeartbeatTimeStamp"].Value),
                        NodeType = result.Members["NodeType"].Value.ToString(),
                        CurrentVersion = reg.CurrentVersion,
                        CurrentBuild = reg.CurrentBuild,
                        InstallationType = reg.InstallationType,
                        ProductName = reg.ProductName,
                        CurrentMajorVersionNumber = reg.CurrentMajorVersionNumber,
                        CurrentMinorVersionNumber = reg.CurrentMinorVersionNumber
                    };
                    int i = ADFSFarm.Servers.FindIndex(c => c.FQDN.ToLower() == props.FQDN.ToLower());
                    if (i < 0)
                        ADFSFarm.Servers.Add(props);
                    else
                        ADFSFarm.Servers[i] = props;
                    xprops = props;
                }
            }
            finally
            {
                if (SPRunSpace != null)
                    SPRunSpace.Close();
            }
            return xprops;
        }

        #endregion

        #region MFA Database
        /// <summary>
        /// CreateMFADatabase method implementation
        /// </summary>
        public string CreateMFADatabase(PSHost host, string _servername, string _databasename, string _username, string _password)
        {
            string sqlscript = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\MFA\SQLTools\mfa-db.sql");
            sqlscript = sqlscript.Replace("%DATABASENAME%", _databasename);
            SqlConnection cnx = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=master;Data Source=" + _servername);
            cnx.Open();
            try
            {
                SqlCommand cmd = new SqlCommand(string.Format("CREATE DATABASE {0}", _databasename), cnx);
                cmd.ExecuteNonQuery();
                SqlCommand cmdl = null;
                if (!string.IsNullOrEmpty(_password))
                    cmdl = new SqlCommand(string.Format("IF NOT EXISTS (SELECT name FROM master.sys.server_principals WHERE name = '{0}') BEGIN CREATE LOGIN [{0}] WITH PASSWORD = '{1}', DEFAULT_DATABASE=[master] END", _username, _password), cnx);
                else
                    cmdl = new SqlCommand(string.Format("IF NOT EXISTS (SELECT name FROM master.sys.server_principals WHERE name = '{0}') BEGIN CREATE LOGIN [{0}] FROM WINDOWS WITH DEFAULT_DATABASE=[master] END", _username), cnx);
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx.Close();
            }
            SqlConnection cnx2 = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=" + _databasename + ";Data Source=" + _servername);
            cnx2.Open();
            try
            {
                try
                {
                    SqlCommand cmd = new SqlCommand(string.Format("CREATE USER [{0}] FOR LOGIN [{0}]", _username), cnx2);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd1 = new SqlCommand(string.Format("ALTER ROLE [db_owner] ADD MEMBER [{0}]", _username), cnx2);
                    cmd1.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd2 = new SqlCommand(string.Format("ALTER ROLE [db_securityadmin] ADD MEMBER [{0}]", _username), cnx2);
                    cmd2.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }

                SqlCommand cmdl = new SqlCommand(sqlscript, cnx2); // Create Tables and more
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx2.Close();
            }
            FlatSQLStore cf = new FlatSQLStore();
            cf.Load(host);
            if (!string.IsNullOrEmpty(_password))
                cf.ConnectionString = "Persist Security Info=True;User ID="+ _username+";Password="+_password+";Initial Catalog=" + _databasename + ";Data Source=" + _servername;
            else
                cf.ConnectionString = "Persist Security Info=False;Integrated Security=SSPI;Initial Catalog=" + _databasename + ";Data Source=" + _servername;
            cf.IsAlwaysEncrypted = false;
            cf.ThumbPrint = string.Empty;
            cf.Update(host);
            return cf.ConnectionString;
        }

        /// <summary>
        /// UpgradeMFADatabase method implementation
        /// </summary>
        public string UpgradeMFADatabase(PSHost host, string servername, string databasename)
        {
            FlatSQLStore cf = new FlatSQLStore();
            cf.Load(host);
            bool encrypt = cf.IsAlwaysEncrypted;
            string sqlscript = string.Empty;
            if (encrypt)
            {
                string keyname = cf.KeyName;
                sqlscript = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\MFA\SQLTools\mfa-db-Encrypted-upgrade.sql");
                sqlscript = sqlscript.Replace("%DATABASENAME%", databasename);
                sqlscript = sqlscript.Replace("%SQLKEY%", keyname);
            }
            else
            {
                sqlscript = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\MFA\SQLTools\mfa-db-upgrade.sql");
                sqlscript = sqlscript.Replace("%DATABASENAME%", databasename);
            }

            SqlConnection cnx2 = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=" + databasename + ";Data Source=" + servername);
            cnx2.Open();
            try
            {
                SqlCommand cmdl = new SqlCommand(sqlscript, cnx2); // Create Tables and more
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx2.Close();
            }
            return cf.ConnectionString;
        }

        /// <summary>
        /// CreateMFAEncryptedDatabase method implementation
        /// </summary>
        public string CreateMFAEncryptedDatabase(PSHost host, string _servername, string _databasename, string _username, string _password, string _keyname, string _thumbprint)
        {
            string _encrypted = GetSQLKeyEncryptedValue("LocalMachine/my/" + _thumbprint.ToUpper());
            string sqlscript = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\MFA\SQLTools\mfa-db-encrypted.sql");
            sqlscript = sqlscript.Replace("%DATABASENAME%", _databasename);
            sqlscript = sqlscript.Replace("%SQLKEY%", _keyname);
            SqlConnection cnx = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=master;Data Source=" + _servername);
            cnx.Open();
            try
            {
                SqlCommand cmd = new SqlCommand(string.Format("CREATE DATABASE {0}", _databasename), cnx);
                cmd.ExecuteNonQuery();
                SqlCommand cmdl = null;
                if (!string.IsNullOrEmpty(_password))
                    cmdl = new SqlCommand(string.Format("IF NOT EXISTS (SELECT name FROM master.sys.server_principals WHERE name = '{0}') BEGIN CREATE LOGIN [{0}] WITH PASSWORD = '{1}', DEFAULT_DATABASE=[master] END", _username, _password), cnx);
                else
                    cmdl = new SqlCommand(string.Format("IF NOT EXISTS (SELECT name FROM master.sys.server_principals WHERE name = '{0}') BEGIN CREATE LOGIN [{0}] FROM WINDOWS WITH DEFAULT_DATABASE=[master] END", _username), cnx);
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx.Close();
            }
            SqlConnection cnx2 = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=" + _databasename + ";Data Source=" + _servername);
            cnx2.Open();
            try
            {
                try
                {
                    SqlCommand cmd = new SqlCommand(string.Format("CREATE USER [{0}] FOR LOGIN [{0}]", _username), cnx2);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd1 = new SqlCommand(string.Format("ALTER ROLE [db_owner] ADD MEMBER [{0}]", _username), cnx2);
                    cmd1.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd2 = new SqlCommand(string.Format("ALTER ROLE [db_securityadmin] ADD MEMBER [{0}]", _username), cnx2);
                    cmd2.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd3 = new SqlCommand(string.Format("GRANT ALTER ANY COLUMN ENCRYPTION KEY TO [{0}]", _username), cnx2);
                    cmd3.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                SqlCommand cmd4 = new SqlCommand(string.Format("CREATE COLUMN MASTER KEY [{0}] WITH (KEY_STORE_PROVIDER_NAME = 'MSSQL_CERTIFICATE_STORE', KEY_PATH = 'LocalMachine/My/{1}')", _keyname, _thumbprint.ToUpper()), cnx2);
                cmd4.ExecuteNonQuery();
                SqlCommand cmd5 = new SqlCommand(string.Format("CREATE COLUMN ENCRYPTION KEY [{0}] WITH VALUES (COLUMN_MASTER_KEY = [{0}], ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = {1})", _keyname, _encrypted), cnx2);
                cmd5.ExecuteNonQuery();
            }
            finally
            {
                cnx2.Close();
            }
            SqlConnection cnx3 = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=" + _databasename + ";Data Source=" + _servername);
            cnx3.Open();
            try
            {
                SqlCommand cmdl = new SqlCommand(sqlscript, cnx3); // create tables and more
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx3.Close();
            }

            FlatSQLStore cf = new FlatSQLStore();
            cf.Load(host);
            if (!string.IsNullOrEmpty(_password))
                cf.ConnectionString = "Persist Security Info=True;User ID=" + _username + ";Password=" + _password + ";Initial Catalog=" + _databasename + ";Data Source=" + _servername +";Column Encryption Setting=enabled";
            else
                cf.ConnectionString = "Persist Security Info=False;Integrated Security=SSPI;Initial Catalog=" + _databasename + ";Data Source=" + _servername + ";Column Encryption Setting=enabled";
            cf.IsAlwaysEncrypted = true;
            cf.ThumbPrint = _thumbprint;
            cf.Update(host);
            return cf.ConnectionString;
        }

        /// <summary>
        /// GetSQLKeyEncryptedValue method implementation
        /// </summary>
        private string GetSQLKeyEncryptedValue(string masterkeypath)
        {
            var randomBytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(randomBytes);
            }
            var provider = new SqlColumnEncryptionCertificateStoreProvider();
            var encryptedKey = provider.EncryptColumnEncryptionKey(masterkeypath, "RSA_OAEP", randomBytes);
            return "0x" + BitConverter.ToString(encryptedKey).Replace("-", "");
        }
        #endregion

        #region MFA SecretKey Database
        /// <summary>
        /// CreateMFADatabase method implementation
        /// </summary>
        public string CreateMFASecretKeysDatabase(PSHost host, string _servername, string _databasename, string _username, string _password)
        {
            string sqlscript = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\MFA\SQLTools\mfa-secretkey-db.sql");
            sqlscript = sqlscript.Replace("%DATABASENAME%", _databasename);
            SqlConnection cnx = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=master;Data Source=" + _servername);
            cnx.Open();
            try
            {
                SqlCommand cmd = new SqlCommand(string.Format("CREATE DATABASE {0}", _databasename), cnx);
                cmd.ExecuteNonQuery();
                SqlCommand cmdl = null;
                if (!string.IsNullOrEmpty(_password))
                    cmdl = new SqlCommand(string.Format("IF NOT EXISTS (SELECT name FROM master.sys.server_principals WHERE name = '{0}') BEGIN CREATE LOGIN [{0}] WITH PASSWORD = '{1}', DEFAULT_DATABASE=[master] END", _username, _password), cnx);
                else
                    cmdl = new SqlCommand(string.Format("IF NOT EXISTS (SELECT name FROM master.sys.server_principals WHERE name = '{0}') BEGIN CREATE LOGIN [{0}] FROM WINDOWS WITH DEFAULT_DATABASE=[master] END", _username), cnx);
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx.Close();
            }
            SqlConnection cnx2 = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=" + _databasename + ";Data Source=" + _servername);
            cnx2.Open();
            try
            {
                try
                {
                    SqlCommand cmd = new SqlCommand(string.Format("CREATE USER [{0}] FOR LOGIN [{0}]", _username), cnx2);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd1 = new SqlCommand(string.Format("ALTER ROLE [db_owner] ADD MEMBER [{0}]", _username), cnx2);
                    cmd1.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd2 = new SqlCommand(string.Format("ALTER ROLE [db_securityadmin] ADD MEMBER [{0}]", _username), cnx2);
                    cmd2.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                SqlCommand cmdl = new SqlCommand(sqlscript, cnx2); // Create Tables and more
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx2.Close();
            }
            FlatCustomSecurity cf = new FlatCustomSecurity();
            cf.Load(host);
            if (!string.IsNullOrEmpty(_password))
                cf.ConnectionString = "Persist Security Info=True;User ID=" + _username + ";Password=" + _password + ";Initial Catalog=" + _databasename + ";Data Source=" + _servername;
            else
                cf.ConnectionString = "Persist Security Info=False;Integrated Security=SSPI;Initial Catalog=" + _databasename + ";Data Source=" + _servername;
            cf.IsAlwaysEncrypted = false;
            cf.ThumbPrint = string.Empty;
            cf.Update(host);
            return cf.Parameters;
        }

        /// <summary>
        /// CreateMFAEncryptedSecretKeysDatabase method implementation
        /// </summary>
        public string CreateMFAEncryptedSecretKeysDatabase(PSHost host, string _servername, string _databasename, string _username, string _password, string _keyname, string _thumbprint)
        {
            string _encrypted = GetSQLKeyEncryptedValue("LocalMachine/my/" + _thumbprint.ToUpper());
            string sqlscript = File.ReadAllText(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\MFA\SQLTools\mfa-secretkey-db-encrypted.sql");
            sqlscript = sqlscript.Replace("%DATABASENAME%", _databasename);
            sqlscript = sqlscript.Replace("%SQLKEY%", _keyname);
            SqlConnection cnx = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=master;Data Source=" + _servername);
            cnx.Open();
            try
            {
                SqlCommand cmd = new SqlCommand(string.Format("CREATE DATABASE {0}", _databasename), cnx);
                cmd.ExecuteNonQuery();
                SqlCommand cmdl = null;
                if (!string.IsNullOrEmpty(_password))
                    cmdl = new SqlCommand(string.Format("IF NOT EXISTS (SELECT name FROM master.sys.server_principals WHERE name = '{0}') BEGIN CREATE LOGIN [{0}] WITH PASSWORD = '{1}', DEFAULT_DATABASE=[master] END", _username, _password), cnx);
                else
                    cmdl = new SqlCommand(string.Format("IF NOT EXISTS (SELECT name FROM master.sys.server_principals WHERE name = '{0}') BEGIN CREATE LOGIN [{0}] FROM WINDOWS WITH DEFAULT_DATABASE=[master] END", _username), cnx);
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx.Close();
            }
            SqlConnection cnx2 = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=" + _databasename + ";Data Source=" + _servername);
            cnx2.Open();
            try
            {
                try
                {
                    SqlCommand cmd = new SqlCommand(string.Format("CREATE USER [{0}] FOR LOGIN [{0}]", _username), cnx2);
                    cmd.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd1 = new SqlCommand(string.Format("ALTER ROLE [db_owner] ADD MEMBER [{0}]", _username), cnx2);
                    cmd1.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd2 = new SqlCommand(string.Format("ALTER ROLE [db_securityadmin] ADD MEMBER [{0}]", _username), cnx2);
                    cmd2.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                try
                {
                    SqlCommand cmd3 = new SqlCommand(string.Format("GRANT ALTER ANY COLUMN ENCRYPTION KEY TO [{0}]", _username), cnx2);
                    cmd3.ExecuteNonQuery();
                }
                catch (Exception)
                {
                    // Nothing : the indicated user is definitely the interactive user so the dbo
                }
                SqlCommand cmd4 = new SqlCommand(string.Format("CREATE COLUMN MASTER KEY [{0}] WITH (KEY_STORE_PROVIDER_NAME = 'MSSQL_CERTIFICATE_STORE', KEY_PATH = 'LocalMachine/My/{1}')", _keyname, _thumbprint.ToUpper()), cnx2);
                cmd4.ExecuteNonQuery();
                SqlCommand cmd5 = new SqlCommand(string.Format("CREATE COLUMN ENCRYPTION KEY [{0}] WITH VALUES (COLUMN_MASTER_KEY = [{0}], ALGORITHM = 'RSA_OAEP', ENCRYPTED_VALUE = {1})", _keyname, _encrypted), cnx2);
                cmd5.ExecuteNonQuery();
            }
            finally
            {
                cnx2.Close();
            }

            SqlConnection cnx3 = new SqlConnection("Integrated Security=SSPI;Persist Security Info=False;Initial Catalog=" + _databasename + ";Data Source=" + _servername);
            cnx3.Open();
            try
            {
                SqlCommand cmdl = new SqlCommand(sqlscript, cnx3);
                cmdl.ExecuteNonQuery();
            }
            finally
            {
                cnx3.Close();
            }

            FlatCustomSecurity cf = new FlatCustomSecurity();
            cf.Load(host);
            if (!string.IsNullOrEmpty(_password))
                cf.ConnectionString = "Persist Security Info=True;User ID=" + _username + ";Password=" + _password + ";Initial Catalog=" + _databasename + ";Data Source=" + _servername + ";Column Encryption Setting=enabled";
            else
                cf.ConnectionString = "Persist Security Info=False;Integrated Security=SSPI;Initial Catalog=" + _databasename + ";Data Source=" + _servername + ";Column Encryption Setting=enabled";
            cf.IsAlwaysEncrypted = true;
            cf.ThumbPrint = _thumbprint;
            cf.Update(host);
            return cf.Parameters;
        }
        #endregion
    }
    #endregion
}