using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml;

public class XmlProcessorResult
{
    public bool Success { get; set; }
    public long ZglvId { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class XmlProcessor
{
    private readonly string _connectionString;
    private readonly string _xsdPath;

    public XmlProcessor(string dbPath, string xsdPath)
    {
        _connectionString = $"Data Source={dbPath}";
        _xsdPath = xsdPath;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public XmlProcessorResult ProcessFile(string xmlPath, ZlList data)
    {
        var result = new XmlProcessorResult();

        if (!ValidateXml(xmlPath, out var xsdErrors))
        {
            result.Success = false;
            result.Errors = xsdErrors;
            return result;
        }

        try
        {
            result.ZglvId = SaveToDatabase(data);

            // Финальная валидация недостатка записей на уровне БД
            var dbErrors = VerifyGenderCountsOnDbLevel(result.ZglvId);
            if (dbErrors.Any())
            {
                result.Success = false;
                result.Errors.AddRange(dbErrors);
                return result;
            }

            result.Success = true;
        }
        catch (SqliteException ex)
        {
            result.Success = false;
            result.Errors.Add($"Откат транзакции СУБД: {ex.Message}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Критическая ошибка сохранения в БД: {ex.Message}");
        }

        return result;
    }

    public bool ValidateXml(string xmlPath, out List<string> validationErrors)
    {
        validationErrors = new List<string>();
        var errors = validationErrors;

        var settings = new XmlReaderSettings();
        settings.Schemas.Add(null, _xsdPath);
        settings.ValidationType = ValidationType.Schema;
        settings.ValidationEventHandler += (sender, args) =>
        {
            errors.Add($"[Строка {args.Exception.LineNumber}, Позиция {args.Exception.LinePosition}]: {args.Message}");
        };

        using (XmlReader reader = XmlReader.Create(xmlPath, settings))
        {
            while (reader.Read()) { }
        }

        return errors.Count == 0;
    }

    public long SaveToDatabase(ZlList data)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        //using (var checkCmd = connection.CreateCommand())
        //{
        //    checkCmd.CommandText = "SELECT COUNT(1) FROM ZGLV WHERE FILENAME = @fileName";
        //    checkCmd.Parameters.AddWithValue("@fileName", data.Header.FileName);
        //    long count = (long)checkCmd.ExecuteScalar();

        //    if (count > 0)
        //    {
        //        // Выбрасываем исключение, которое поймает UI и выведет в Log
        //        throw new InvalidOperationException($"Файл '{data.Header.FileName}' уже был загружен ранее в БД!");
        //    }
        //}

        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA foreign_keys = ON;";
            pragmaCmd.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();

        try
        {
            // 1. Если файл с таким именем уже есть — удаляем его из ZGLV.
            // Благодаря каскадному удалению (ON DELETE CASCADE) SQLite 
            // автоматически удалит все старые EVENT, PERS и CONTACTS для этого файла!
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM ZGLV WHERE FILENAME = @fileName";
                deleteCmd.Parameters.AddWithValue("@fileName", data.Header.FileName);
                deleteCmd.ExecuteNonQuery();
            }

            // 2. Вставляем новый ZGLV (получит свежий ID)
            using var cmdZglv = connection.CreateCommand();
            cmdZglv.Transaction = transaction;
            cmdZglv.CommandText = @"
                INSERT INTO ZGLV (VERSION, DATA, FILENAME, YEAR, CODE_MO) 
                VALUES (@v, @d, @f, @y, @c);
                SELECT last_insert_rowid();";

            cmdZglv.Parameters.AddWithValue("@v", data.Header.Version);
            cmdZglv.Parameters.AddWithValue("@d", data.Header.Data);
            cmdZglv.Parameters.AddWithValue("@f", data.Header.FileName);
            cmdZglv.Parameters.AddWithValue("@y", data.Header.Year);
            cmdZglv.Parameters.AddWithValue("@c", data.Header.CodeMo);

            long zglvId = (long)cmdZglv.ExecuteScalar();

            foreach (var evt in data.Events)
            {
                int actualM = evt.Persons?.Count(p => p.Gender == 1) ?? 0;
                int actualW = evt.Persons?.Count(p => p.Gender == 2) ?? 0;

                Console.WriteLine($"Анализ XML-блока EVENT DISP={evt.Disp}");
                Console.WriteLine($"├─ Мужчины (KOL_M): Заявлено в заголовке = {evt.KolM}, Найдено элементов PERS = {actualM}");
                Console.WriteLine($"└─ Женщины (KOL_W): Заявлено в заголовке = {evt.KolW}, Найдено элементов PERS = {actualW}");
                using var cmdEvt = connection.CreateCommand();
                cmdEvt.Transaction = transaction;
                cmdEvt.CommandText = @"
                    INSERT INTO EVENT (ZGLV_ID, DISP, KOL_M, KOL_W) 
                    VALUES (@zId, @disp, @km, @kw);
                    SELECT last_insert_rowid();";

                cmdEvt.Parameters.AddWithValue("@zId", zglvId);
                cmdEvt.Parameters.AddWithValue("@disp", evt.Disp);
                cmdEvt.Parameters.AddWithValue("@km", evt.KolM);
                cmdEvt.Parameters.AddWithValue("@kw", evt.KolW);

                long eventId = (long)cmdEvt.ExecuteScalar();

                if (evt.Persons == null) continue;

                using var cmdPers = connection.CreateCommand();
                cmdPers.Transaction = transaction;
                cmdPers.CommandText = @"
                    INSERT INTO PERS (
                        EVENT_ID, N_ZAP, ID_PAC, W, DR, SMO, VPOLIS, SPOLIS, NPOLIS, 
                        QUARTER, MONTH, LPU1, DEPTH, SS_DOC, SS_DOC_D, PRVS_D, DS_D, PLACE_D, ID_TFOMS, COMMENT
                    ) VALUES (
                        @eId, @nZap, @idPac, @w, @dr, @smo, @vpolis, @spolis, @npolis, 
                        @quarter, @month, @lpu1, @depth, @ssDoc, @ssDocD, @prvsD, @dsD, @placeD, @idTfoms, @comment
                    );
                    SELECT last_insert_rowid();";

                var pEId = cmdPers.Parameters.Add("@eId", SqliteType.Integer);
                var pNZap = cmdPers.Parameters.Add("@nZap", SqliteType.Integer);
                var pIdPac = cmdPers.Parameters.Add("@idPac", SqliteType.Text);
                var pW = cmdPers.Parameters.Add("@w", SqliteType.Integer);
                var pDr = cmdPers.Parameters.Add("@dr", SqliteType.Text);
                var pSmo = cmdPers.Parameters.Add("@smo", SqliteType.Text);
                var pVPolis = cmdPers.Parameters.Add("@vpolis", SqliteType.Integer);
                var pSPolis = cmdPers.Parameters.Add("@spolis", SqliteType.Text);
                var pNPolis = cmdPers.Parameters.Add("@npolis", SqliteType.Text);
                var pQuarter = cmdPers.Parameters.Add("@quarter", SqliteType.Integer);
                var pMonth = cmdPers.Parameters.Add("@month", SqliteType.Integer);
                var pLpu1 = cmdPers.Parameters.Add("@lpu1", SqliteType.Text);
                var pDepth = cmdPers.Parameters.Add("@depth", SqliteType.Text);
                var pSsDoc = cmdPers.Parameters.Add("@ssDoc", SqliteType.Text);
                var pSsDocD = cmdPers.Parameters.Add("@ssDocD", SqliteType.Text);
                var pPrvsD = cmdPers.Parameters.Add("@prvsD", SqliteType.Integer);
                var pDsD = cmdPers.Parameters.Add("@dsD", SqliteType.Text);
                var pPlaceD = cmdPers.Parameters.Add("@placeD", SqliteType.Integer);
                var pIdTfoms = cmdPers.Parameters.Add("@idTfoms", SqliteType.Text);
                var pComment = cmdPers.Parameters.Add("@comment", SqliteType.Text);

                using var cmdContact = connection.CreateCommand();
                cmdContact.Transaction = transaction;
                cmdContact.CommandText = @"
                    INSERT INTO CONTACTS (PERS_ID, PHONE_F, PHONE_M, EMAIL, ADDRESS)
                    VALUES (@pId, @pf, @pm, @em, @addr);";

                var cPId = cmdContact.Parameters.Add("@pId", SqliteType.Integer);
                var cPf = cmdContact.Parameters.Add("@pf", SqliteType.Text);
                var cPm = cmdContact.Parameters.Add("@pm", SqliteType.Text);
                var cEm = cmdContact.Parameters.Add("@em", SqliteType.Text);
                var cAddr = cmdContact.Parameters.Add("@addr", SqliteType.Text);

                foreach (var pers in evt.Persons)
                {
                    pEId.Value = eventId;
                    pNZap.Value = pers.NZap;
                    pIdPac.Value = pers.IdPac;
                    pW.Value = pers.Gender;
                    pDr.Value = pers.Dr;
                    pSmo.Value = pers.Smo;
                    pVPolis.Value = pers.VPolis;
                    pSPolis.Value = (object)pers.SPolis ?? DBNull.Value;
                    pNPolis.Value = pers.NPolis;
                    pQuarter.Value = (object)pers.Quarter ?? DBNull.Value;
                    pMonth.Value = (object)pers.Month ?? DBNull.Value;
                    pLpu1.Value = pers.Lpu1;
                    pDepth.Value = pers.Depth;
                    pSsDoc.Value = pers.SsDoc;
                    pSsDocD.Value = (object)pers.SsDocD ?? DBNull.Value;
                    pPrvsD.Value = (object)pers.PrvsD ?? DBNull.Value;
                    pDsD.Value = (object)pers.DsD ?? DBNull.Value;
                    pPlaceD.Value = (object)pers.PlaceD ?? DBNull.Value;
                    pIdTfoms.Value = (object)pers.IdTfoms ?? DBNull.Value;
                    pComment.Value = (object)pers.Comment ?? DBNull.Value;

                    long persId = (long)cmdPers.ExecuteScalar();

                    if (pers.Contacts != null)
                    {
                        cPId.Value = persId;
                        cPf.Value = pers.Contacts.PhoneF?.Count > 0 ? string.Join(";", pers.Contacts.PhoneF) : DBNull.Value;
                        cPm.Value = pers.Contacts.PhoneM?.Count > 0 ? string.Join(";", pers.Contacts.PhoneM) : DBNull.Value;
                        cEm.Value = pers.Contacts.Email?.Count > 0 ? string.Join(";", pers.Contacts.Email) : DBNull.Value;
                        cAddr.Value = (object)pers.Contacts.Address ?? DBNull.Value;

                        cmdContact.ExecuteNonQuery();
                    }
                }
            }

            var checkErrors = VerifyGenderCountsOnDbLevel(zglvId, connection, transaction);
            if (checkErrors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("; ", checkErrors));
            }

            transaction.Commit();
            return zglvId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public List<string> VerifyGenderCountsOnDbLevel(long zglvId, SqliteConnection conn = null, SqliteTransaction tran = null)
    {
        var errors = new List<string>();
        bool isExternalConn = conn != null;

        var connection = isExternalConn ? conn : new SqliteConnection(_connectionString);
        if (!isExternalConn) connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            if (tran != null) command.Transaction = tran;

            command.CommandText = @"
            SELECT 
                e.ID AS EventId,
                e.DISP,
                e.KOL_M AS ExpectedM,
                e.KOL_W AS ExpectedW,
                COALESCE(SUM(CASE WHEN p.W = 1 THEN 1 ELSE 0 END), 0) AS ActualM,
                COALESCE(SUM(CASE WHEN p.W = 2 THEN 1 ELSE 0 END), 0) AS ActualW
            FROM EVENT e
            LEFT JOIN PERS p ON e.ID = p.EVENT_ID
            WHERE e.ZGLV_ID = @zglvId
            GROUP BY e.ID, e.DISP, e.KOL_M, e.KOL_W;";

            command.Parameters.AddWithValue("@zglvId", zglvId);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                long eventId = reader.GetInt64(0);
                string disp = reader.GetString(1);
                int expectedM = reader.GetInt32(2);
                int expectedW = reader.GetInt32(3);
                int actualM = reader.GetInt32(4);
                int actualW = reader.GetInt32(5);

                // Вывод статистики в консоль для наглядности
                Console.WriteLine($"[Статистика EVENT ID={eventId}, DISP={disp}]:");
                Console.WriteLine($"├─ Мужчины (KOL_M): ожидалось = {expectedM}, фактически в PERS = {actualM} {(expectedM == actualM ? "✓" : "✗")}");
                Console.WriteLine($"└─ Женщины (KOL_W): ожидалось = {expectedW}, фактически в PERS = {actualW} {(expectedW == actualW ? "✓" : "✗")}");

                if (expectedM != actualM)
                {
                    errors.Add($"[EVENT ID={eventId}, DISP={disp}]: Несоответствие мужчин. Ожидалось KOL_M={expectedM}, фактически в PERS={actualM}");
                }
                if (expectedW != actualW)
                {
                    errors.Add($"[EVENT ID={eventId}, DISP={disp}]: Несоответствие женщин. Ожидалось KOL_W={expectedW}, фактически в PERS={actualW}");
                }
            }
        }
        finally
        {
            if (!isExternalConn) connection.Dispose();
        }

        return errors;
    }
}