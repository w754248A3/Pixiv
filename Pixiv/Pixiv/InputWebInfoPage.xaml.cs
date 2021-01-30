using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace Pixiv
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    public partial class InputWebInfoPage : ContentPage
    {
        public InputWebInfoPage()
        {
            InitializeComponent();

            m_text.Text = InputData.WebInfo;
        }

        void OnInput(object sender, EventArgs e)
        {
            string s = m_text.Text;

            if (WebInfoHelper.TryCreate(s, out _, out string message))
            {
                InputData.WebInfo = s;

                DisplayAlert("消息", "输入成功", "确定");
            }
            else
            {
                DisplayAlert("错误", message, "确定");
            }
        }
    }
}