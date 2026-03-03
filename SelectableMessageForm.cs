using System.Windows.Forms;

namespace BSH_Import_Utility
{
    public partial class SelectableMessageForm : Form
    {
        public SelectableMessageForm(string title, string message)
        {
            InitializeComponent();

            Text = title;
            txtMessage.Text = message;
        }
    }
}