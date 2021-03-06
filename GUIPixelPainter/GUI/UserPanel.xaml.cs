﻿using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GUIPixelPainter.GUI
{
    /// <summary>
    /// Interaction logic for UsersPanel.xaml
    /// </summary>
    public partial class UserPanel : UserControl
    {
        private class User
        {
            public Guid internalId;
            public string name;
            public string authKey;
            public string authToken;
            public string phpSessId;
            public bool isEnabled;
            public Status status;
        }

        private List<User> users = new List<User>();
        private bool ignoreEvents = false;
        private System.Windows.Media.Brush inactiveTask = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 0xcc, 0xcc, 0xcc));

        public GUIDataExchange DataExchange { get; set; }
        public Launcher Launcher { get; set; }

        public UserPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// return an immutable copy of users
        /// </summary>
        /// <returns></returns>
        public List<GUIUser> GetUsers()
        {
            List<GUIUser> converted = new List<GUIUser>();
            foreach (User user in users)
            {
                converted.Add(new GUIUser(user.internalId, user.name, user.authKey, user.authToken, user.phpSessId, user.status, user.isEnabled));
            }
            return converted;
        }

        public Guid GetSelectedUserGuidIfAny()
        {
            if (userList.SelectedItem == null)
                return Guid.Empty;
            return Guid.Parse(((userList.SelectedItem as StackPanel).Children[1] as TextBlock).Text);
        }

        public void SetUserTokens(Guid id, string phpSessId, string authToken)
        {
            var user = GetUser(id);
            if (user == null)
                return;
            user.authToken = authToken;
            user.phpSessId = phpSessId;
            UpdateUserList();
            DataExchange.UpdateUsersFromGUI();
            Launcher.Save();
        }

        public void SetUserStatus(Guid id, Status status)
        {
            var user = GetUser(id);
            if (user == null)
                return;
            if (user.status == status) //HACK hacky fix of a bug where you can't change user data while the user is enabled. May still be a problem in rare cases
                return;
            user.status = status;
            UpdateUserList();
        }

        public void DisableAllUsers()
        {
            foreach (var user in users)
            {
                user.isEnabled = false;
            }
            DataExchange.UpdateUsersFromGUI();
            UpdateUserList();
        }

        public void AddNewUser(GUIUser user)
        {
            User newUser = new User()
            {
                internalId = user.InternalId,
                name = user.Name,
                authKey = user.AuthKey,
                authToken = user.AuthToken,
                phpSessId = user.PhpSessId,
                isEnabled = user.Enabled,
                status = user.Status
            };
            users.Add(newUser);
            DataExchange.UpdateUsersFromGUI();
            UpdateUserList();
        }

        private User GetSelectedUser()
        {
            return GetUser(Guid.Parse(((userList.SelectedItem as StackPanel).Children[1] as TextBlock).Text));
        }

        private User GetUser(Guid id)
        {
            return users.Find((a) => a.internalId == id);
        }

        private bool UserExists(Guid id)
        {
            return users.Find((a) => a.internalId == id) != null;
        }

        private void OnNewUserClick(object sender, RoutedEventArgs e)
        {
            if (ignoreEvents)
                return;
            string name = "New user";
            Guid id = Guid.NewGuid();
            User user = new User() { name = name, internalId = id };
            users.Add(user);

            DataExchange.UpdateUsersFromGUI();
            UpdateUserList();
        }

        private void UpdateUserList()
        {
            //remove old users
            for (int i = userList.Items.Count - 1; i >= 0; i--)
            {
                StackPanel item = userList.Items[i] as StackPanel;
                Guid id = Guid.Parse((item.Children[1] as TextBlock).Text);
                if (!UserExists(id))
                    userList.Items.Remove(item);
            }

            //add new users
            foreach (User user in users)
            {
                bool exists = false;
                foreach (StackPanel item in userList.Items)
                {
                    if (Guid.Parse((item.Children[1] as TextBlock).Text) == user.internalId)
                    {
                        exists = true;
                        //update name and status of existing user
                        var itemText = (item.Children[0] as Label);
                        itemText.Content = user.name;
                        if (user.status == Status.CONNECTING || user.status == Status.OPEN)
                            itemText.Foreground = Brushes.Green;
                        else if (user.status == Status.CLOSEDDISCONNECT || user.status == Status.CLOSEDERROR)
                            itemText.Foreground = Brushes.Red;
                        else
                            itemText.Foreground = inactiveTask;
                        break;
                    }
                }
                if (exists)
                    continue;

                Label label = new Label() { Content = user.name };
                TextBlock id = new TextBlock() { Text = user.internalId.ToString(), Visibility = Visibility.Collapsed };
                StackPanel panel = new StackPanel();
                panel.Children.Add(label);
                panel.Children.Add(id);
                userList.Items.Add(panel);
            }
            UpdateUserSettingsPanel();
        }

        private void UpdateUserSettingsPanel()
        {
            ignoreEvents = true;

            if (userList.SelectedItem == null)
            {
                userName.Text = string.Empty;
                userProxy.Text = string.Empty;
                authKey.Text = string.Empty;
                authToken.Text = string.Empty;
                enableUser.IsChecked = false;

                userName.IsEnabled = false;
                loginButton.IsEnabled = false;
                userProxy.IsEnabled = false;
                authKey.IsEnabled = false;
                authToken.IsEnabled = false;
                phpSessId.IsEnabled = false;
                enableUser.IsEnabled = false;
                deleteUser.IsEnabled = false;
                userStatus.Content = "Status: ";

                ignoreEvents = false;
                return;
            }
            User selectedUser = GetSelectedUser();

            userName.Text = selectedUser.name;
            authKey.Text = selectedUser.authKey;
            authToken.Text = selectedUser.authToken;
            phpSessId.Text = selectedUser.phpSessId;
            enableUser.IsChecked = selectedUser.isEnabled;
            userStatus.Content = "Status: " + selectedUser.status.ToString();

            userName.IsEnabled = true;
            loginButton.IsEnabled = !selectedUser.isEnabled;
            userProxy.IsEnabled = true;
            authKey.IsEnabled = true;
            authToken.IsEnabled = true;
            phpSessId.IsEnabled = true;
            enableUser.IsEnabled = true;
            deleteUser.IsEnabled = true;

            ignoreEvents = false;
        }

        private void OnDeleteUserClick(object sender, RoutedEventArgs e)
        {
            if (ignoreEvents)
                return;
            User user = GetSelectedUser();
            users.Remove(user);
            DataExchange.UpdateUsersFromGUI();
            UpdateUserList();
        }

        private void OnUserSelection(object sender, SelectionChangedEventArgs e)
        {
            if (ignoreEvents)
                return;
            UpdateUserSettingsPanel();
        }

        private void OnTextBoxGotFocus(object sender, RoutedEventArgs e)
        {
            userName.SelectAll();
            userProxy.SelectAll();
            authKey.SelectAll();
            authToken.SelectAll();
        }

        private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
        {
            if (ignoreEvents)
                return;
            if (userList.SelectedIndex == -1)
                return;

            User selectedUser = GetSelectedUser();

            selectedUser.name = userName.Text;
            selectedUser.authKey = authKey.Text;
            selectedUser.authToken = authToken.Text;
            selectedUser.phpSessId = phpSessId.Text;
            DataExchange.UpdateUsersFromGUI();
            if (sender.Equals(userName))
                UpdateUserList();
        }

        private void OnEnableUser(object sender, RoutedEventArgs e)
        {
            if (ignoreEvents)
                return;

            if (userList.SelectedItem == null)
                return;
            GetSelectedUser().isEnabled = true;
            loginButton.IsEnabled = false;
            DataExchange.UpdateUsersFromGUI();
        }

        private void OnDisableUser(object sender, RoutedEventArgs e)
        {
            if (ignoreEvents)
                return;

            if (userList.SelectedItem == null)
                return;
            GetSelectedUser().isEnabled = false;
            loginButton.IsEnabled = true;
            DataExchange.UpdateUsersFromGUI();
        }

        private void OpenBrowserClick(object sender, RoutedEventArgs e)
        {

            Browser browser = new Browser();
            browser.ShowDialog();
            if (!browser.Success)
            {
                MessageBox.Show("Log in before closing the window");
                return;
            }

            User user = GetSelectedUser();
            user.authKey = browser.AuthKey;
            user.authToken = browser.AuthToken;
            user.phpSessId = browser.PHPSESSID;
            UpdateUserSettingsPanel();

            enableUser.IsChecked = true;
        }
    }
}