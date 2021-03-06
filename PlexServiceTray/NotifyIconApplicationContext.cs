﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.ServiceProcess;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using PlexServiceCommon;
using System.ServiceModel;
using System.Windows;

namespace PlexServiceTray
{
    /// <summary>
    /// Tray icon context
    /// </summary>
    class NotifyIconApplicationContext : ApplicationContext
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer _components = null;

        private System.Windows.Forms.NotifyIcon _notifyIcon;

        //private readonly static TimeSpan _timeOut = TimeSpan.FromSeconds(2);

        private PlexServiceCommon.Interface.ITrayInteraction _plexService;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (_components != null))
            {
                Disconnect();
                _components.Dispose();
                _notifyIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        public NotifyIconApplicationContext()
        {
            initializeContext();
            Connect();
        }

        /// <summary>
        /// Setup our tray icon
        /// </summary>
        private void initializeContext()
        {
            _components = new System.ComponentModel.Container();
            _notifyIcon = new NotifyIcon(_components);
            _notifyIcon.ContextMenuStrip = new ContextMenuStrip();
            _notifyIcon.Icon = new Icon( Properties.Resources.PlexService, SystemInformation.SmallIconSize);
            _notifyIcon.Text = "Manage Plex Media Server Service";
            _notifyIcon.Visible = true;
            _notifyIcon.MouseClick += NotifyIcon_Click;
            _notifyIcon.MouseDoubleClick += NotifyIcon_DoubleClick;
            _notifyIcon.ContextMenuStrip.Opening += ContextMenuStrip_Opening;
        }

        /// <summary>
        /// Connect to WCF service
        /// </summary>
        private void Connect()
        {
            var localSettings = ConnectionSettings.Load();
            //Create a NetTcp binding to the service and set some appropriate timeouts.
            //Use reliable connection so we know when we have been disconnected
            var plexServiceBinding = new NetTcpBinding();
            plexServiceBinding.OpenTimeout = TimeSpan.FromSeconds(2); 
            plexServiceBinding.CloseTimeout = TimeSpan.FromSeconds(2);
            plexServiceBinding.SendTimeout = TimeSpan.FromSeconds(2);
            plexServiceBinding.ReliableSession.Enabled = true;
            plexServiceBinding.ReliableSession.InactivityTimeout = TimeSpan.FromMinutes(1);
            //Generate the endpoint from the local settings
            var plexServiceEndpoint = new EndpointAddress(localSettings.getServiceAddress());

            TrayCallback callback = new TrayCallback();
            callback.StateChange += Callback_StateChange;
            var client = new TrayInteractionClient(callback, plexServiceBinding, plexServiceEndpoint);

            //Make a channel factory so we can create the link to the service
            //var plexServiceChannelFactory = new ChannelFactory<PlexServiceCommon.Interface.ITrayInteraction>(plexServiceBinding, plexServiceEndpoint);

            _plexService = null;

            try
            {
                _plexService = client.ChannelFactory.CreateChannel(); //plexServiceChannelFactory.CreateChannel();
                _plexService.Subscribe();
                //If we lose connection to the service, set the object to null so we will know to reconnect the next time the tray icon is clicked
                ((ICommunicationObject)_plexService).Faulted += (s, e) => _plexService = null;
                ((ICommunicationObject)_plexService).Closed += (s, e) => _plexService = null;


            }
            catch
            {
                if (_plexService != null)
                {
                    _plexService = null;
                }
            }
        }

        private void Callback_StateChange(object sender, StatusChangeEventArgs e)
        {
            _notifyIcon.ShowBalloonTip(2000, "Plex Service", e.Description, ToolTipIcon.Info);
        }

        /// <summary>
        /// Disconnect from WCF service
        /// </summary>
        private void Disconnect()
        {
            //try and be nice...
            if (_plexService != null)
            {
                try
                {
                    _plexService.UnSubscribe();
                    ((ICommunicationObject)_plexService).Close();
                }
                catch { }
            }
            _plexService = null;
        }

        /// <summary>
        /// Open the context menu on right click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void NotifyIcon_Click(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                _notifyIcon.ContextMenuStrip.Show();
            }
        }

        /// <summary>
        /// Opens the web manager on a double left click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NotifyIcon_DoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenManager_Click(sender, e);
            }
        }

        /// <summary>
        /// build the context menu each time it opens to ensure appropriate options
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void ContextMenuStrip_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = false;
            _notifyIcon.ContextMenuStrip.Items.Clear();

            //see if we are still connected.
            if (_plexService == null)
            {
                Connect();
            }

            if (_plexService != null)// && ((ICommunicationObject)_plexService).State == CommunicationState.Opened)
            {
                try
                {
                    var state = _plexService.GetStatus();
                    switch (state)
                    {
                        case PlexState.Running:
                            _notifyIcon.ContextMenuStrip.Items.Add("Open Web Manager", null, OpenManager_Click);
                            _notifyIcon.ContextMenuStrip.Items.Add("Stop Plex", null, StopPlex_Click);
                            break;
                        case PlexState.Stopped:
                            _notifyIcon.ContextMenuStrip.Items.Add("Start Plex", null, StartPlex_Click);
                            break;
                        case PlexState.Pending:
                            _notifyIcon.ContextMenuStrip.Items.Add("Restart Pending");
                            break;
                        case PlexState.Stopping:
                            _notifyIcon.ContextMenuStrip.Items.Add("Stopping");
                            break;
                        default:
                            _notifyIcon.ContextMenuStrip.Items.Add("Plex state unknown");
                            break;
                    }
                    _notifyIcon.ContextMenuStrip.Items.Add("View Logs", null, ViewLogs_Click);
                    _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
                    _notifyIcon.ContextMenuStrip.Items.Add("Settings", null, SettingsCommand);
                }
                catch
                {
                    Disconnect();
                    _notifyIcon.ContextMenuStrip.Items.Add("Unable to connect to service. Check settings");
                }
            }
            else
            {
                Disconnect();
                _notifyIcon.ContextMenuStrip.Items.Add("Unable to connect to service. Check settings");

            }
            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _notifyIcon.ContextMenuStrip.Items.Add("Connection Settings", null, ConnectionSettingsCommand);
            _notifyIcon.ContextMenuStrip.Items.Add("About", null, AboutCommand);
            _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
            _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, ExitCommand);
        }

        /// <summary>
        /// Show the settings dialogue
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SettingsCommand(object sender, EventArgs e)
        {
            if (_plexService != null)
            {
                Settings settings = null;
                try
                {
                    settings = Settings.Deserialize(_plexService.GetSettings());
                }
                catch 
                {
                    Disconnect();
                }

                if (settings != null)
                {
                    //Save the current server port setting for reference
                    int oldPort = settings.ServerPort;
                    SettingsWindowViewModel settingsViewModel = new SettingsWindowViewModel(settings);
                    settingsViewModel.AuxAppStartRequest += (s, args) =>
                    {
                        var requester = s as AuxiliaryApplicationViewModel;
                        if (requester != null)
                        {
                            _plexService.StartAuxApp(requester.Name);
                            requester.Running = _plexService.IsAuxAppRunning(requester.Name);
                        }
                    };
                    settingsViewModel.AuxAppStopRequest += (s, args) =>
                    {
                        var requester = s as AuxiliaryApplicationViewModel;
                        if(requester != null)
                        {
                            _plexService.StopAuxApp(requester.Name);
                            requester.Running = _plexService.IsAuxAppRunning(requester.Name);
                        }
                    };
                    settingsViewModel.AuxAppCheckRunRequest += (s, args) =>
                    {
                        var requester = s as AuxiliaryApplicationViewModel;
                        if (requester != null)
                        {
                            requester.Running = _plexService.IsAuxAppRunning(requester.Name);
                        }
                    };
                    SettingsWindow settingsWindow = new SettingsWindow(settingsViewModel);
                    if (settingsWindow.ShowDialog() == true)
                    {
                        PlexState status = PlexState.Pending;
                        try
                        {
                            _plexService.SetSettings(settingsViewModel.WorkingSettings.Serialize());
                            status = _plexService.GetStatus();
                        }
                        catch(Exception ex)
                        {
                            Disconnect();
                            System.Windows.MessageBox.Show("Unable to save settings" + Environment.NewLine + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                        }     
                        //The only setting that would require a restart of the service is the listening port.
                        //If that gets changed notify the user to restart the service from the service snap in
                        if (settingsViewModel.WorkingSettings.ServerPort != oldPort)
                        {
                            System.Windows.MessageBox.Show("Server port changed! You will need to restart the service from the services snap in for the change to be applied", "Settings changed!", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Show the connection settings dialogue
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ConnectionSettingsCommand(object sender, EventArgs e)
        {
            ConnectionSettingsWindow connectionSettingsWindow = new ConnectionSettingsWindow();
            if (connectionSettingsWindow.ShowDialog() == true)
            {
                //if the user saved the settings, then reconnect using the new values
                try
                {
                    Disconnect();
                    Connect();
                }
                catch { }
            }
        }

        /// <summary>
        /// Open the About dialog
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AboutCommand(object sender, EventArgs e)
        {
            AboutWindow.ShowAboutDialog();
        }

        /// <summary>
        /// Close the notify icon
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ExitCommand(object sender, EventArgs e)
        {
            Disconnect();
            ExitThread();
        }

        /// <summary>
        /// Start Plex
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartPlex_Click(object sender, EventArgs e)
        {
            //start it
            if (_plexService != null)
            {
                try
                {
                    _plexService.Start();
                }
                catch 
                {
                    Disconnect();
                }
            }
        }

        /// <summary>
        /// Stop Plex
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StopPlex_Click(object sender, EventArgs e)
        {
            //stop it
            if (_plexService != null)
            {
                try
                {
                    _plexService.Stop();
                }
                catch 
                {
                    Disconnect();
                }
            }
        }

        /// <summary>
        /// Try to open the web manager
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenManager_Click(object sender, EventArgs e)
        {
            //The web manager should be located at the server address in the connection settings
            Process.Start("http://" + ConnectionSettings.Load().ServerAddress + ":32400/web");
        }

        /// <summary>
        /// View the server log file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ViewLogs_Click(object sender, EventArgs e)
        {
            //Show the data from the server in notepad, but don't save it to disk locally.
            try
            {
                NotepadHelper.ShowMessage(_plexService.GetLog(), "Plex Service Log");
            }
            catch
            {
                Disconnect();
            }
        }

    }
}
