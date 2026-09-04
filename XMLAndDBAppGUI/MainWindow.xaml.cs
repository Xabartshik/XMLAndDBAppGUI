using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System;
using System.Data;
using System.IO;
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

                Log("\n[ИНФО] База данных успешно очищена. Счетчики ID сброшены.", Colors.Yellow);
                TxtStatus.Text = "БД очищена";
            }
            catch (Exception ex)
            {
                Log($"\n❌ Ошибка при очистке БД: {ex.Message}", Colors.Red);
                TxtStatus.Text = "Ошибка очистки";
            }
        }

        /// <summary>
        /// Выполняет полную очистку таблиц SQLite и сброс счетчиков AUTOINCREMENT
        /// </summary>
        private void ClearDatabase()
        {
            string connString = $"Data Source={_dbPath}";

            using (var conn = new SqliteConnection(connString))
            {
                conn.Open();

                using (var tran = conn.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.Transaction = tran;
                            cmd.CommandText = @"
                        PRAGMA foreign_keys = OFF;

                        DELETE FROM CONTACTS;
                        DELETE FROM PERS;
                        DELETE FROM EVENT;
                        DELETE FROM ZGLV;

                        DELETE FROM sqlite_sequence WHERE name IN ('CONTACTS', 'PERS', 'EVENT', 'ZGLV');

                        PRAGMA foreign_keys = ON;";

                            cmd.ExecuteNonQuery();
                        }

                        tran.Commit();
                    }
                    catch
                    {
                        tran.Rollback();
                        throw;
                    }
                }

                // Вызываем VACUUM для сжатия файла базы данных на диске
                using (var cmdVacuum = conn.CreateCommand())
                {
                    cmdVacuum.CommandText = "VACUUM;";
                    cmdVacuum.ExecuteNonQuery();
                }
            }
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

                // 3. Сохранение в БД SQLite под контролем триггеров
                Log("\n3. Сохранение данных в SQLite в единой транзакции...");
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
        /// Вычитывает данные из SQLite для отображения во вкладках DataGrid
        /// </summary>
        private void LoadDataToGrids()
        {
            string connString = $"Data Source={_dbPath}";
            using var conn = new SqliteConnection(connString);
            conn.Open();

            // Таблица Events
            using var cmdEvt = conn.CreateCommand();
            cmdEvt.CommandText = @"
                SELECT e.ID, z.FILENAME, e.DISP, e.KOL_M AS 'Заявлено М', e.KOL_W AS 'Заявлено Ж',
                       (SELECT COUNT(*) FROM PERS p WHERE p.EVENT_ID = e.ID AND p.W = 1) AS 'Факт М',
                       (SELECT COUNT(*) FROM PERS p WHERE p.EVENT_ID = e.ID AND p.W = 2) AS 'Факт Ж'
                FROM EVENT e
                JOIN ZGLV z ON e.ZGLV_ID = z.ID;";
            var dtEvents = new DataTable();
            using (var reader = cmdEvt.ExecuteReader())
            {
                dtEvents.Load(reader);
            }
            DgEvents.ItemsSource = dtEvents.DefaultView;

            // Таблица Persons
            using var cmdPers = conn.CreateCommand();
            cmdPers.CommandText = @"
                SELECT p.ID, p.EVENT_ID, p.N_ZAP, p.ID_PAC, 
                       CASE WHEN p.W = 1 THEN 'Муж' ELSE 'Жен' END AS Пол,
                       p.DR, p.NPOLIS, p.LPU1, p.DS_D 
                FROM PERS p LIMIT 500;";
            var dtPersons = new DataTable();
            using (var reader = cmdPers.ExecuteReader())
            {
                dtPersons.Load(reader);
            }
            DgPersons.ItemsSource = dtPersons.DefaultView;
        }
    }
}