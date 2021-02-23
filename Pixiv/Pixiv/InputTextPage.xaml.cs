using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Pixiv
{
    public readonly struct InputTextPageResult
    {
        public InputTextPageResult(string result, string[] list)
        {
            Result = result;
            List = list;
        }

        public string Result { get; }

        public string[] List { get; }
    }

    public sealed class InputTextPageInfo
    {
        public InputTextPageInfo(string label, string item, string[] list)
        {
            Label = label;
        
            Item = item;
            
            List = list;

            TaskSource = new TaskCompletionSource<InputTextPageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string Label { get; }

        public string Item { get; }

        public string[] List { get; }

        public Task<InputTextPageResult> Task => TaskSource.Task;

        public TaskCompletionSource<InputTextPageResult> TaskSource { get; }
    }

    public sealed class InputTextPageBindingData
    {
        public InputTextPageBindingData(string text, Command<InputTextPageBindingData> command)
        {
            Text = text;
            Command = command;
        }

        public string Text { get; set; }

        public Command<InputTextPageBindingData> Command { get; set; }
    }

    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class InputTextPage : ContentPage
    {
        InputTextPageInfo m_info;

        public InputTextPage(InputTextPageInfo info)
        {
            InitializeComponent();

            Init(info);
        }


        void Init(InputTextPageInfo info)
        {
            m_info = info;

            m_label.Text = info.Label;

            m_entry.Text = info.Item;

            var collection = new ObservableCollection<InputTextPageBindingData>();


            m_collectionView.ItemsSource = collection;

            var command = new Command<InputTextPageBindingData>((item) =>
            {
                collection.Remove(item);
            },
            (item) => true);

            var list = info.List.ToHashSet().Select((s) => new InputTextPageBindingData(s, command)).ToArray();

            Array.ForEach(list, (item) => collection.Add(item));
        }

        void OnInputOK(object sender, EventArgs e)
        {
            string result = m_entry.Text;

            m_info.TaskSource.TrySetResult(
                new InputTextPageResult(
                    result,
                    m_collectionView.ItemsSource.OfType<InputTextPageBindingData>().Select((item) => item.Text).Append(result).ToHashSet().ToArray()));

            Navigation.PopModalAsync();
        }

        protected override bool OnBackButtonPressed()
        {
            m_info.TaskSource.TrySetCanceled();

            return base.OnBackButtonPressed();
        }

        void OnSelectChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_collectionView.SelectedItem is InputTextPageBindingData item)
            {
                m_entry.Text = item.Text;
            }
        }
    }
}