﻿using System;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Connection.Protocol;
using mRemoteNG.Connection.Protocol.RDP;
using mRemoteNG.Container;
using mRemoteNG.Messages;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.Panels;
using mRemoteNG.UI.Window;
using TabPage = Crownwood.Magic.Controls.TabPage;


namespace mRemoteNG.Connection
{
	public class ConnectionInitiator : IConnectionInitiator
    {
        private readonly PanelAdder _panelAdder = new PanelAdder();

        public void OpenConnection(ContainerInfo containerInfo, ConnectionInfo.Force force = ConnectionInfo.Force.None)
        {
            OpenConnection(containerInfo, force, null);
        }

        public void OpenConnection(ConnectionInfo connectionInfo)
        {
            try
            {
                OpenConnection(connectionInfo, ConnectionInfo.Force.None);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.strConnectionOpenFailed, ex);
            }
        }

        public void OpenConnection(ConnectionInfo connectionInfo, ConnectionInfo.Force force)
        {
            try
            {
                OpenConnection(connectionInfo, force, null);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.strConnectionOpenFailed, ex);
            }
        }

        public bool SwitchToOpenConnection(ConnectionInfo connectionInfo)
        {
            var interfaceControl = FindConnectionContainer(connectionInfo);
            if (interfaceControl == null) return false;
            var connectionWindow = (ConnectionWindow)interfaceControl.FindForm();
            connectionWindow?.Focus();
            var findForm = (ConnectionWindow)interfaceControl.FindForm();
            findForm?.Show(FrmMain.Default.pnlDock);
            var tabPage = (TabPage)interfaceControl.Parent;
            tabPage.Selected = true;
            return true;
        }

        #region Private
        private void OpenConnection(ContainerInfo containerInfo, ConnectionInfo.Force force, Form conForm)
        {
            var children = containerInfo.Children;
            if (children.Count == 0) return;
            foreach (var child in children)
            {
                var childAsContainer = child as ContainerInfo;
                if (childAsContainer != null)
                    OpenConnection(childAsContainer, force, conForm);
                else
                    OpenConnection(child, force, conForm);
            }
        }

        private void OpenConnection(ConnectionInfo connectionInfo, ConnectionInfo.Force force, Form conForm)
        {
            try
            {
                if (connectionInfo.Hostname == "" && connectionInfo.Protocol != ProtocolType.IntApp)
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, Language.strConnectionOpenFailedNoHostname);
                    return;
                }

                StartPreConnectionExternalApp(connectionInfo);

                if ((force & ConnectionInfo.Force.DoNotJump) != ConnectionInfo.Force.DoNotJump)
                {
                    if (SwitchToOpenConnection(connectionInfo))
                        return;
                }

                var protocolFactory = new ProtocolFactory();
                var newProtocol = protocolFactory.CreateProtocol(connectionInfo);

                var connectionPanel = SetConnectionPanel(connectionInfo, force);
                var connectionForm = SetConnectionForm(conForm, connectionPanel);
                var connectionContainer = SetConnectionContainer(connectionInfo, connectionForm);
                SetConnectionFormEventHandlers(newProtocol, connectionForm);
                SetConnectionEventHandlers(newProtocol);
                BuildConnectionInterfaceController(newProtocol, connectionContainer);

                newProtocol.Force = force;

                if (newProtocol.Initialize() == false)
                {
                    newProtocol.Close();
                    return;
                }

                if (newProtocol.Connect() == false)
                {
                    newProtocol.Close();
                    return;
                }

                connectionInfo.OpenConnections.Add(newProtocol);
                FrmMain.Default.SelectedConnection = connectionInfo;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.strConnectionOpenFailed, ex);
            }
        }

        private static void StartPreConnectionExternalApp(ConnectionInfo connectionInfo)
        {
            if (connectionInfo.PreExtApp == "") return;
            var extA = Runtime.ExternalToolsService.GetExtAppByName(connectionInfo.PreExtApp);
            extA?.Start(connectionInfo);
        }

        private static InterfaceControl FindConnectionContainer(ConnectionInfo connectionInfo)
        {
            if (connectionInfo.OpenConnections.Count <= 0) return null;
            for (var i = 0; i <= Runtime.WindowList.Count - 1; i++)
            {
                var window = Runtime.WindowList[i] as ConnectionWindow;
                var connectionWindow = window;
                if (connectionWindow?.TabController == null) continue;
                foreach (TabPage t in connectionWindow.TabController.TabPages)
                {
                    var ic = t.Controls[0] as InterfaceControl;
                    if (ic == null) continue;
                    if (ic.Protocol.Info == connectionInfo)
                    {
                        return ic;
                    }
                }
            }
            return null;
        }

        private static string SetConnectionPanel(ConnectionInfo connectionInfo, ConnectionInfo.Force force)
        {
            var connectionPanel = "";
            if (connectionInfo.Panel == "" || (force & ConnectionInfo.Force.OverridePanel) == ConnectionInfo.Force.OverridePanel | Settings.Default.AlwaysShowPanelSelectionDlg)
            {
                var frmPnl = new frmChoosePanel();
                if (frmPnl.ShowDialog() == DialogResult.OK)
                {
                    connectionPanel = frmPnl.Panel;
                }
            }
            else
            {
                connectionPanel = connectionInfo.Panel;
            }
            return connectionPanel;
        }

        private Form SetConnectionForm(Form conForm, string connectionPanel)
        {
            var connectionForm = conForm ?? Runtime.WindowList.FromString(connectionPanel);

            if (connectionForm == null)
                connectionForm = _panelAdder.AddPanel(connectionPanel);
            else
                ((ConnectionWindow)connectionForm).Show(FrmMain.Default.pnlDock);

            connectionForm.Focus();
            return connectionForm;
        }

        private static Control SetConnectionContainer(ConnectionInfo connectionInfo, Form connectionForm)
        {
            Control connectionContainer = ((ConnectionWindow)connectionForm).AddConnectionTab(connectionInfo);

            if (connectionInfo.Protocol != ProtocolType.IntApp) return connectionContainer;

            var extT = Runtime.ExternalToolsService.GetExtAppByName(connectionInfo.ExtApp);

            if(extT == null) return connectionContainer;

            if (extT.Icon != null)
                ((TabPage)connectionContainer).Icon = extT.Icon;

            return connectionContainer;
        }

        private static void SetConnectionFormEventHandlers(ProtocolBase newProtocol, Form connectionForm)
        {
            newProtocol.Closed += ((ConnectionWindow)connectionForm).Prot_Event_Closed;
        }

        private static void SetConnectionEventHandlers(ProtocolBase newProtocol)
        {
            newProtocol.Disconnected += Prot_Event_Disconnected;
            newProtocol.Connected += Prot_Event_Connected;
            newProtocol.Closed += Prot_Event_Closed;
            newProtocol.ErrorOccured += Prot_Event_ErrorOccured;
        }

        private static void BuildConnectionInterfaceController(ProtocolBase newProtocol, Control connectionContainer)
        {
            newProtocol.InterfaceControl = new InterfaceControl(connectionContainer, newProtocol);
        }
        #endregion

        #region Event handlers
        private static void Prot_Event_Disconnected(object sender, string disconnectedMessage)
        {
            try
            {
                if (sender is VncSharp.RemoteDesktop)
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, string.Format(Language.strProtocolEventDisconnected, @"VncSharp Disconnected."), true);
                    return;
                }

                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, string.Format(Language.strProtocolEventDisconnected, disconnectedMessage), true);

                var prot = (ProtocolBase)sender;
                if (prot.Info.Protocol != ProtocolType.RDP) return;
                var reasonCode = disconnectedMessage.Split("\r\n".ToCharArray())[0];
                var desc = disconnectedMessage.Replace("\r\n", " ");

                if (Convert.ToInt32(reasonCode) > 3)
                    Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, Language.strRdpDisconnected + Environment.NewLine + desc);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.strProtocolEventDisconnectFailed, ex);
            }
        }

        private static void Prot_Event_Closed(object sender, EventArgs args)
        {
            try
            {
                var prot = (ProtocolBase)sender;
                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, Language.strConnenctionCloseEvent, true);
                string connDetail;
                if (prot.Info.Hostname == "" && prot.Info.Protocol == ProtocolType.IntApp)
                    connDetail = prot.Info.ExtApp;
                else if (prot.Info.Hostname != "")
                    connDetail = prot.Info.Hostname;
                else
                    connDetail = "UNKNOWN";

                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, 
                    string.Format(Language.strConnenctionClosedByUser, connDetail, prot.Info.Protocol, Environment.UserName));
                prot.Info.OpenConnections.Remove(prot);

                if (prot.Info.PostExtApp == "") return;
                var extA = Runtime.ExternalToolsService.GetExtAppByName(prot.Info.PostExtApp);
                extA?.Start(prot.Info);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.strConnenctionCloseEventFailed, ex);
            }
        }

        private static void Prot_Event_Connected(object sender, EventArgs args)
        {
            var prot = (ProtocolBase)sender;
            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, Language.strConnectionEventConnected, true);
            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, 
                string.Format(Language.strConnectionEventConnectedDetail, prot.Info.Hostname, prot.Info.Protocol, Environment.UserName, prot.Info.Description, prot.Info.UserField));
        }

        private static void Prot_Event_ErrorOccured(object sender, string errorMessage)
        {
            try
            {
                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, Language.strConnectionEventErrorOccured, true);
                var prot = (ProtocolBase)sender;

                if (prot.Info.Protocol != ProtocolType.RDP) return;
	            var errorMessageAsInt = Convert.ToInt32(errorMessage);

				if (errorMessageAsInt > -1)
                    Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, 
                        string.Format(Language.strConnectionRdpErrorDetail, errorMessage, RdpErrorTranslator.Translate(errorMessageAsInt)));
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.strConnectionEventConnectionFailed, ex);
            }
        }
        #endregion
    }
}