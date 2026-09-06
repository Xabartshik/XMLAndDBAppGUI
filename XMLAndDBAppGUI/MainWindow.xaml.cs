using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Serialization;

namespace XMLAndDBAppGUI
{
    public partial class MainWindow : Window
    {
        private readonly string _dbPath = Path.Combine(AppContext.BaseDirectory, "test_db.db");

        public MainWindow()
        {
            InitializeComponent();
            string baseDir = AppContext.BaseDirectory;

            string xmlFilePath = Path.Combine(baseDir, "LPQM750025T75_193.xml");
            string xsdFilePath = Path.Combine(baseDir, "schema.xsd");

            TxtXmlPath.Text = File.Exists(xmlFilePath) ? xmlFilePath : string.Empty;
            TxtXsdPath.Text = File.Exists(xsdFilePath) ? xsdFilePath : string.Empty;
        }

        private void BtnBrowseXml_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "XML файлы (*.xml)|*.xml|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                TxtXmlPath.Text = dlg.FileName;
            }
        }

        private void BtnBrowseXsd_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "XSD схемы (*.xsd)|*.xsd|Все файлы (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                TxtXsdPath.Text = dlg.FileName;
            }
        }
        private void BtnClearDb_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Вы действительно хотите полностью очистить базу данных?\nВсе записи ZGLV, EVENT, PERS и CONTACTS будут удалены.",
                "Подтверждение очистки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            try
            {
                ClearDatabase();

                // Очищаем отображение в таблицах UI
                DgEvents.ItemsSource = null;
                DgPersons.ItemsSource = null;

                Log("\n[ИНФО] База данных успешно очищена.", Colors.Yellow);
                TxtStatus.Text = "БД очищена";
            }
            catch (Exception ex)
            {
                Log($"\n❌ Ошибка при очистке БД: {ex.Message}", Colors.Red);
                TxtStatus.Text = "Ошибка очистки";
            }
        }

        /// <summary>
        /// Выполняет полную очистку таблиц через ORM.
        /// </summary>
        private void ClearDatabase()
        {
            using var database = RegistryDbContextFactory.Create(_dbPath);
            using var transaction = database.Database.BeginTransaction();
            database.Registries.RemoveRange(database.Registries);
            database.SaveChanges();
            transaction.Commit();
        }
        private async void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Document.Blocks.Clear();
            Log("=== Старт обработки XML-реестра ===");

            string xmlPath = TxtXmlPath.Text;
            string xsdPath = TxtXsdPath.Text;

            if (!File.Exists(xmlPath))
            {
                Log("[ОШИБКА] Указанный XML-файл не найден!", Colors.Red);
                return;
            }

            if (!File.Exists(xsdPath))
            {
                Log("[ОШИБКА] Указанная XSD-схема не найдена!", Colors.Red);
                return;
            }

            BtnProcess.IsEnabled = false;
            TxtStatus.Text = "Обработка...";

            try
            {
                var processor = new XmlProcessor(_dbPath, xsdPath);

                // 1. Валидация XSD
                Log("1. Проверка файла по XSD-схеме...");
                bool isValidXsd = processor.ValidateXml(xmlPath, out var xsdErrors);

                if (!isValidXsd)
                {
                    Log("❌ ОШИБКА ВАЛИДАЦИИ XSD:", Colors.Red);
                    foreach (var err in xsdErrors)
                    {
                        Log($"   • {err}", Colors.Red);
                    }
                    TxtStatus.Text = "Ошибка XSD";
                    return;
                }
                Log("✔ Файл полностью соответствует XSD-схеме.", Colors.LightGreen);

                // 2. Десериализация XML
                Log("\n2. Десериализация XML в C#-объекты...");
                ZlList xmlData;
                XmlSerializer serializer = new XmlSerializer(typeof(ZlList));
                using (var reader = new StreamReader(xmlPath, Encoding.GetEncoding("windows-1251")))
                {
                    xmlData = (ZlList)serializer.Deserialize(reader);
                }
                Log($"✔ Успешно прочитан заголовок файла: {xmlData.Header.FileName}");

                // 3. Сохранение в БД через ORM в единой транзакции
                Log("\n3. Сохранение данных через ORM в единой транзакции...");
                var result = processor.ProcessFile(xmlPath, xmlData);

                if (result.Success)
                {
                    Log($"\n[УСПЕХ] Транзакция зафиксирована! Присвоен ZGLV_ID = {result.ZglvId}", Colors.LightGreen);
                    TxtStatus.Text = "Успешно сохранено";

                    // Обновляем таблицы в UI
                    LoadDataToGrids();
                }
                else
                {
                    Log("\n❌ [ОТКАТ ТРАНЗАКЦИИ] Данные не сохранены из-за ошибок контроля целостности:", Colors.Red);
                    foreach (var err in result.Errors)
                    {
                        Log($"   • {err}", Colors.OrangeRed);
                    }
                    TxtStatus.Text = "Ошибка БД / Откат транзакции";
                }
            }
            catch (Exception ex)
            {
                Log($"\n❌ Критическое исключение: {ex.Message}", Colors.Red);
                TxtStatus.Text = "Критическая ошибка";
            }
            finally
            {
                BtnProcess.IsEnabled = true;
            }
        }

        private void Log(string message, Color? color = null)
        {
            var range = new TextRange(TxtLog.Document.ContentEnd, TxtLog.Document.ContentEnd)
            {
                Text = $"{DateTime.Now:HH:mm:ss} {message}\n"
            };

            // По умолчанию используется зеленый цвет логов в стиле консоли/Discord Discord-Green (#2ECC71)
            Color textColor = color ?? Color.FromRgb(46, 204, 113);
            range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(textColor));

            TxtLog.ScrollToEnd();
        }

        /// <summary>
        /// Вычитывает данные через ORM для отображения во вкладках DataGrid.
        /// </summary>
        private void LoadDataToGrids()
        {
            using var database = RegistryDbContextFactory.Create(_dbPath);

            DgEvents.ItemsSource = database.Events
                .Select(item => new EventGridRow
                {
                    Id = item.Id,
                    FileName = item.Registry.FileName,
                    Disp = item.Disp,
                    DeclaredMen = item.KolM,
                    DeclaredWomen = item.KolW,
                    ActualMen = item.Persons.Count(person => person.Gender == 1),
                    ActualWomen = item.Persons.Count(person => person.Gender == 2)
                })
                .ToList();

            DgPersons.ItemsSource = database.Persons
                .OrderBy(item => item.Id)
                .Take(500)
                .Select(item => new PersonGridRow
                {
                    Id = item.Id,
                    EventId = item.EventId,
                    NZap = item.NZap,
                    IdPac = item.IdPac,
                    Gender = item.Gender == 1 ? "Муж" : "Жен",
                    Dr = item.Dr,
                    NPolis = item.NPolis,
                    Lpu1 = item.Lpu1,
                    DsD = item.DsD
                })
                .ToList();
        }
    }
}
