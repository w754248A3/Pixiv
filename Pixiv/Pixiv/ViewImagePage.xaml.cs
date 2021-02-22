using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Pixiv
{
    public sealed class ViewImagePageInfo
    {
        public ViewImagePageInfo(byte[] buffer, Task<byte[]> bufferTask, Action clicked)
        {
            Buffer = buffer;
            BufferTask = bufferTask;
            Clicked = clicked;

            TaskSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public byte[] Buffer { get; }

        public Task<byte[]> BufferTask { get; }

        public Action Clicked { get; }

        public Task Task => TaskSource.Task;


        public TaskCompletionSource<object> TaskSource { get; }
    }


    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ViewImagePage : ContentPage
    {
        ViewImagePageInfo m_info;



        public ViewImagePage(ViewImagePageInfo info)
        {
            InitializeComponent();
            
            m_info = info;

            m_image.Source = ImageSource.FromStream(() => new MemoryStream(info.Buffer));

            m_info.BufferTask.ContinueWith((t) =>
            {
                var buffer = t.Result;

                MainThread.BeginInvokeOnMainThread(() =>
                {

                    m_image.Source = ImageSource.FromStream(() => new MemoryStream(buffer));
                });
            });

        }

        protected override bool OnBackButtonPressed()
        {
            m_info.TaskSource.TrySetResult(default);

            return base.OnBackButtonPressed();
        }

        void OnClicked(object sender, EventArgs e)
        {
            m_image.IsEnabled = false;

            m_info.Clicked();

        }
    }
}