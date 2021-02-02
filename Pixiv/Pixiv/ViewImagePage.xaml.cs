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
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class ViewImagePage : ContentPage
    {
        Action m_clicked;



        public ViewImagePage(Task<byte[]> task, Action clicked)
        {
            InitializeComponent();

            m_clicked = clicked;

            task.ContinueWith((t) =>
            {
                var buffer = t.Result;

                MainThread.BeginInvokeOnMainThread(() =>
                {

                    m_image.Source = ImageSource.FromStream(() => new MemoryStream(buffer));
                });
            });

        }

        void OnClicked(object sender, EventArgs e)
        {
            if (m_clicked is null)
            {

            }
            else
            {
                m_clicked();

                m_clicked = null;
            }
        }
    }
}