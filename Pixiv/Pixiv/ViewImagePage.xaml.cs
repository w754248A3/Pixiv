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



        public ViewImagePage(Data data, Action clicked)
        {
            InitializeComponent();

            m_clicked = clicked;

            m_image.Source = ImageSource.FromStream(() => new MemoryStream(data.Buffer));
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