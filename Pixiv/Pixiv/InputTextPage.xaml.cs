using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

            m_collectionView.ItemsSource = info.List.ToHashSet().ToArray();
        }

        void OnInputOK(object sender, EventArgs e)
        {
            string result = m_entry.Text;

            m_info.TaskSource.TrySetResult(
                new InputTextPageResult(
                    result,
                    m_collectionView.ItemsSource
                            .OfType<string>().Append(result).ToHashSet().ToArray()));
        }

        protected override bool OnBackButtonPressed()
        {
            m_info.TaskSource.TrySetCanceled();

            return base.OnBackButtonPressed();
        }

        void OnSelectChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_collectionView.SelectedItem is string s)
            {
                m_entry.Text = s;
            }
        }
    }
}