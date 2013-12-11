using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.EntityClient;
using System.Data.Metadata.Edm;
using System.Data.Objects;
using System.Linq;
using System.Reflection;
using Mediaportal.TV.Server.TVDatabase.Entities;
using Mediaportal.TV.Server.TVLibrary.Interfaces.Logging;

namespace Mediaportal.TV.Server.TVDatabase.EntityModel.ObjContext
{
  public static class ObjectContextManager
  {
    public enum SupportedDatabaseType
    {
      None,
      MySQL,
      MSSQL,
      MSSQLCE,
      SQLite
    }

    #region Static fields

    public delegate DbConnection CreateConnectionDelegate();
    private static CreateConnectionDelegate _externalDbCreator;

    private static readonly object _syncObj = new object();
    private static MetadataWorkspace _metadataWorkspace = null;
    private static bool _initialized = false;
    private static SupportedDatabaseType _databaseType;

    #endregion

    #region Global initalization

    /// <summary>
    /// Sets an external <see cref="CreateConnectionDelegate"/> that returns a <see cref="DbConnection"/>. This can be used
    /// by hosting environments to supply their own database to be used for EF.
    /// </summary>
    /// <param name="creatorDelegate">Creator</param>
    public static void SetDbConnectionCreator(CreateConnectionDelegate creatorDelegate)
    {
      _externalDbCreator = creatorDelegate;
    }

    /// <summary>
    /// Creates the model, checks for existing database and creates it, if not present.
    /// </summary>
    private static void Initialize()
    {
      if (_initialized)
        return;
      try
      {
        var model = GetModel();
        _databaseType = DetectDatabase(model);
        if (CheckCreateDatabase(model))
        {
          Log.Info("DataBase does not exist. Creating database...");
          CreateIndexes(model);
          DropConstraints(model);
          SetupStaticValues(model);
        }
        _initialized = true;
      }
      catch (Exception ex)
      {
        Log.Error(ex, "ObjectContextManager : error opening database. Is the SQL engine running ?");
        throw;
      }
    }

    /// <summary>
    /// Checks if the database exists and creates it if required.
    /// </summary>
    /// <param name="model">Model</param>
    /// <returns><c>true</c> if database was created</returns>
    private static bool CheckCreateDatabase(Model model)
    {
      // This is a special workaround for System.Data.SQLite provider, it does not support creation of database.
      if (_databaseType == SupportedDatabaseType.SQLite)
      {
        // We supply a database with all structures, but without data. So we check if the database is already filled using a table 
        // that contains static values.
        int cnt = model.ExecuteStoreQuery<int>("SELECT COUNT(*) FROM LnbTypes").First();
        // If we get a count the table exists but is empty. So we have the same situation as after model.CreateDatabase();
        // If table does not exist, we will get an exception. Handling it would not help here, because we cannot create the tables using EF.
        return cnt == 0;
      }

      if (!model.DatabaseExists())
      {
        model.CreateDatabase();
        return true;
      }

      return false;
    }

    private static SupportedDatabaseType DetectDatabase(Model model)
    {
      return DetectDatabase(((EntityConnection)model.Connection).StoreConnection);
    }

    private static SupportedDatabaseType DetectDatabase(DbConnection connection)
    {
      string conType = connection.GetType().ToString();
      if (conType.Contains("MySqlClient"))
        return SupportedDatabaseType.MySQL;
      if (conType.Contains("SQLite"))
        return SupportedDatabaseType.SQLite;
      if (conType.Contains("SqlServerCe"))
        return SupportedDatabaseType.MSSQLCE;
      if (conType.Contains("SqlClient"))
        return SupportedDatabaseType.MSSQL;

      return SupportedDatabaseType.None;
    }

    private static void CreateIndexes(Model model)
    {
      TryCreateIndex(model, "CardGroupMaps", "IdCard");
      TryCreateIndex(model, "CardGroupMaps", "IdCardGroup");
      TryCreateIndex(model, "CanceledSchedules", "IdSchedule");
      TryCreateIndex(model, "Channels", "MediaType");
      TryCreateIndex(model, "ChannelMaps", "IdCard");
      TryCreateIndex(model, "ChannelMaps", "IdChannel");
      TryCreateIndex(model, "ChannelLinkageMaps", "IdLinkedChannel");
      TryCreateIndex(model, "ChannelLinkageMaps", "IdPortalChannel");
      TryCreateIndex(model, "Conflicts", "IdCard");
      TryCreateIndex(model, "Conflicts", "IdChannel");
      TryCreateIndex(model, "Conflicts", "IdSchedule");
      TryCreateIndex(model, "Conflicts", "IdConflictingSchedule");
      TryCreateIndex(model, "DisEqcMotors", "IdCard");
      TryCreateIndex(model, "DisEqcMotors", "IdSatellite");
      TryCreateIndex(model, "GroupMaps", "IdGroup");
      TryCreateIndex(model, "GroupMaps", "IdChannel");
      TryCreateIndex(model, "Histories", "IdChannel");
      TryCreateIndex(model, "Histories", "IdProgramCategory");
      TryCreateIndex(model, "KeywordMaps", "IdKeyword");
      TryCreateIndex(model, "KeywordMaps", "IdChannelGroup");
      TryCreateIndex(model, "PersonalTVGuideMaps", "IdProgram");
      TryCreateIndex(model, "PersonalTVGuideMaps", "IdKeyword");
      TryCreateIndex(model, "Programs", "IdChannel");
      TryCreateIndex(model, "Programs", "IdProgramCategory");
      TryCreateIndex(model, "Programs", new List<string> { "StartTime", "EndTime" });
      TryCreateIndex(model, "ProgramCredits", "IdProgram");
      TryCreateIndex(model, "Recordings", "IdChannel");
      TryCreateIndex(model, "Recordings", "IdSchedule");
      TryCreateIndex(model, "Recordings", "IdProgramCategory");
      TryCreateIndex(model, "RecordingCredits", "IdRecording");
      TryCreateIndex(model, "Schedules", "IdChannel");
      TryCreateIndex(model, "Schedules", "IdParentSchedule");
      TryCreateIndex(model, "TuningDetails", "IdChannel");
      TryCreateIndex(model, "TvMovieMappings", "IdChannel");
    }

    private static void DropConstraints(Model model)
    {
      TryDropConstraint(model, "Recordings", "ChannelRecording");
      TryDropConstraint(model, "Recordings", "RecordingProgramCategory");
      TryDropConstraint(model, "Recordings", "ScheduleRecording");
    }

    /// <summary>
    /// Creates a <see cref="Model"/> instance, which can use the _externalDbCreator.
    /// </summary>
    /// <returns></returns>
    private static Model GetModel()
    {
      Model model = _externalDbCreator != null
        ? new Model(BuildEFConnection(_externalDbCreator()))
        : new Model();
      return model;
    }

    /// <summary>
    /// Helper method to construct a valid <see cref="EntityConnection"/> from a given <paramref name="dbConnection"/>.
    /// This method supports different database types: SQLServer, MySQL, SQLServerCE, the valid model metadata is chosen
    /// automatically.
    /// </summary>
    /// <param name="dbConnection">Connection</param>
    /// <returns>EntityConnection</returns>
    public static EntityConnection BuildEFConnection(DbConnection dbConnection)
    {
      var database = DetectDatabase(dbConnection);
      lock (_syncObj)
      {
        if (database != _databaseType || _metadataWorkspace == null)
        {
          List<string> metadata = new List<string>
          {
            "res://Mediaportal.TV.Server.TVDatabase.EntityModel/Model.csdl",
            "res://Mediaportal.TV.Server.TVDatabase.EntityModel/Model.msl",
            // Select matching ssdl based on the detected database type.
            string.Format("res://Mediaportal.TV.Server.TVDatabase.EntityModel/Mediaportal.TV.Server.TVDatabase.EntityModel.Model.{0}.ssdl", database)
          };
          _databaseType = database;
          _metadataWorkspace = new MetadataWorkspace(metadata, new[] { Assembly.GetExecutingAssembly() });
        }
        return new EntityConnection(_metadataWorkspace, dbConnection);
      }
    }

    #endregion

    public static void SetupStaticValues(Model ctx)
    {
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 1, Name = "Universal", LowBandFrequency = 9750000, HighBandFrequency = 10600000, SwitchFrequency = 11700000, IsBandStacked = false, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 2, Name = "C-Band", LowBandFrequency = 5150000, HighBandFrequency = 5650000, SwitchFrequency = 18000000, IsBandStacked = false, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 3, Name = "10700 MHz", LowBandFrequency = 10700000, HighBandFrequency = 11200000, SwitchFrequency = 18000000, IsBandStacked = false, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 4, Name = "10750 MHz", LowBandFrequency = 10750000, HighBandFrequency = 11250000, SwitchFrequency = 18000000, IsBandStacked = false, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 5, Name = "11250 MHz (NA Legacy)", LowBandFrequency = 11250000, HighBandFrequency = 11750000, SwitchFrequency = 18000000, IsBandStacked = false, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 6, Name = "11300 MHz", LowBandFrequency = 11300000, HighBandFrequency = 11800000, SwitchFrequency = 18000000, IsBandStacked = false, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 7, Name = "DishPro Band Stacked FSS", LowBandFrequency = 10750000, HighBandFrequency = 13850000, SwitchFrequency = 18000000, IsBandStacked = true, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 8, Name = "DishPro Band Stacked DBS", LowBandFrequency = 11250000, HighBandFrequency = 14350000, SwitchFrequency = 18000000, IsBandStacked = true, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 9, Name = "NA Band Stacked FSS", LowBandFrequency = 10750000, HighBandFrequency = 10175000, SwitchFrequency = 18000000, IsBandStacked = true, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 10, Name = "NA Band Stacked DBS", LowBandFrequency = 11250000, HighBandFrequency = 10675000, SwitchFrequency = 18000000, IsBandStacked = true, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 11, Name = "Sadoun Band Stacked", LowBandFrequency = 10100000, HighBandFrequency = 10750000, SwitchFrequency = 18000000, IsBandStacked = true, IsToroidal = false });
      ctx.LnbTypes.AddObject(new LnbType { IdLnbType = 12, Name = "C-Band Band Stacked", LowBandFrequency = 5150000, HighBandFrequency = 5750000, SwitchFrequency = 18000000, IsBandStacked = true, IsToroidal = false });
      ctx.SaveChanges();
    }

    private static readonly object _createDbContextLock = new object();

    public static Model CreateDbContext()
    {
      lock (_createDbContextLock)
      {
        // seems a new instance per WCF is the way to go, since a shared context will end up in EF errors.
        Initialize();
      }

      var model = GetModel();

      //model.ContextOptions.DefaultQueryPlanCachingSetting = true;
      model.ContextOptions.LazyLoadingEnabled = false;
      model.ContextOptions.ProxyCreationEnabled = false;

      model.CanceledSchedules.MergeOption = MergeOption.NoTracking;
      model.CardGroupMaps.MergeOption = MergeOption.NoTracking;
      model.CardGroups.MergeOption = MergeOption.NoTracking;
      model.Cards.MergeOption = MergeOption.NoTracking;
      model.ChannelGroups.MergeOption = MergeOption.NoTracking;
      model.ChannelLinkageMaps.MergeOption = MergeOption.NoTracking;
      model.ChannelMaps.MergeOption = MergeOption.NoTracking;
      model.Channels.MergeOption = MergeOption.NoTracking;
      model.Conflicts.MergeOption = MergeOption.NoTracking;
      model.DisEqcMotors.MergeOption = MergeOption.NoTracking;
      model.Favorites.MergeOption = MergeOption.NoTracking;
      model.GroupMaps.MergeOption = MergeOption.NoTracking;
      model.Histories.MergeOption = MergeOption.NoTracking;
      model.KeywordMaps.MergeOption = MergeOption.NoTracking;
      model.Keywords.MergeOption = MergeOption.NoTracking;
      model.PendingDeletions.MergeOption = MergeOption.NoTracking;
      model.PersonalTVGuideMaps.MergeOption = MergeOption.NoTracking;
      model.ProgramCategories.MergeOption = MergeOption.NoTracking;
      model.ProgramCredits.MergeOption = MergeOption.NoTracking;
      model.Programs.MergeOption = MergeOption.NoTracking;
      model.RecordingCredits.MergeOption = MergeOption.NoTracking;
      model.Recordings.MergeOption = MergeOption.NoTracking;
      model.RuleBasedSchedules.MergeOption = MergeOption.NoTracking;
      model.Satellites.MergeOption = MergeOption.NoTracking;
      model.ScheduleRulesTemplates.MergeOption = MergeOption.NoTracking;
      model.Schedules.MergeOption = MergeOption.NoTracking;
      model.Settings.MergeOption = MergeOption.NoTracking;
      model.SoftwareEncoders.MergeOption = MergeOption.NoTracking;
      model.Timespans.MergeOption = MergeOption.NoTracking;
      model.TuningDetails.MergeOption = MergeOption.NoTracking;
      model.TvMovieMappings.MergeOption = MergeOption.NoTracking;
      model.Versions.MergeOption = MergeOption.NoTracking;
      return model;
    }

    /// <summary>
    /// Tries to drop an existing constraint (i.e. if it was created internally by EF).
    /// </summary>
    /// <param name="model">Model</param>
    /// <param name="tableName">Table name</param>
    /// <param name="constraintName">Constraint name</param>
    private static void TryDropConstraint(Model model, string tableName, string constraintName)
    {
      var database = DetectDatabase(model);
      try
      {
        string sql = database == SupportedDatabaseType.MySQL ?
          "ALTER TABLE {0} DROP FOREIGN KEY {1}" : // MySQL syntax
          "ALTER TABLE {0} DROP CONSTRAINT {1}";   // Microsoft-SQLServer(+CE) syntax
        model.ExecuteStoreCommand(string.Format(sql, tableName, constraintName));
      }
      catch (Exception ex)
      {
        Log.Warn(ex, "Failed to drop constraint {0}.{1}", tableName, constraintName);
      }
    }

    /// <summary>
    /// Tries to create an Index on given <paramref name="tableName"/> for a single column <paramref name="indexedColumn"/>.
    /// </summary>
    /// <param name="model">Model</param>
    /// <param name="tableName">Table name</param>
    /// <param name="indexedColumn">Column name</param>
    private static void TryCreateIndex(Model model, string tableName, string indexedColumn)
    {
      TryCreateIndex(model, tableName, new List<string> { indexedColumn });
    }

    /// <summary>
    /// Tries to create an Index on given <paramref name="tableName"/> for multiple columns (<paramref name="indexedColumns"/>).
    /// </summary>
    /// <param name="model">Model</param>
    /// <param name="tableName">Table name</param>
    /// <param name="indexedColumns">Column names</param>
    private static void TryCreateIndex(Model model, string tableName, IList<string> indexedColumns)
    {
      try
      {
        model.ExecuteStoreCommand(string.Format("CREATE INDEX IX_{0}_{1} ON {0} ({2})", tableName, string.Join("_", indexedColumns), string.Join(", ", indexedColumns)).ToUpperInvariant());
      }
      catch (Exception ex)
      {
        Log.Warn(ex, "Failed to create index on table {0} for column(s) {1}", tableName, string.Join(", ", indexedColumns));
      }
    }

    /*public static void Init(string[] mappingAssemblies, bool recreateDatabaseIfExist = false, bool lazyLoadingEnabled = true)
      {
          Init(DefaultConnectionStringName, mappingAssemblies, recreateDatabaseIfExist, lazyLoadingEnabled);
      }        

      public static void Init(string connectionStringName, string[] mappingAssemblies, bool recreateDatabaseIfExist = false, bool lazyLoadingEnabled = true)
      {
          AddConfiguration(connectionStringName, mappingAssemblies, recreateDatabaseIfExist, lazyLoadingEnabled);
      }       

      public static void InitStorage(IObjectContextStorage storage)
      {
          if (storage == null) 
          {
              throw new ArgumentNullException("storage");
          }
          if ((Storage != null) && (Storage != storage))
          {
              throw new ApplicationException("A storage mechanism has already been configured for this application");
          }            
          Storage = storage;
      }

      /// <summary>
      /// The default connection string name used if only one database is being communicated with.
      /// </summary>
      public static readonly string DefaultConnectionStringName = "Model";        

      /// <summary>
      /// Used to get the current object context session if you're communicating with a single database.
      /// When communicating with multiple databases, invoke <see cref="CurrentFor()" /> instead.
      /// </summary>
      public static System.Data.Objects.ObjectContext Current
      {
          get
          {
              return CurrentFor(DefaultConnectionStringName);
          }
      }

      /// <summary>
      /// Used to get the current ObjectContext associated with a key; i.e., the key 
      /// associated with an object context for a specific database.
      /// 
      /// If you're only communicating with one database, you should call <see cref="Current" /> instead,
      /// although you're certainly welcome to call this if you have the key available.
      /// </summary>
      public static System.Data.Objects.ObjectContext CurrentFor(string key)
      {
          if (string.IsNullOrEmpty(key))
          {
              throw new ArgumentNullException("key");
          }

          if (Storage == null)
          {
              throw new ApplicationException("An IObjectContextStorage has not been initialized");
          }

          System.Data.Objects.ObjectContext context = null;
          lock (_syncLock)
          {
              if (!objectContextBuilders.ContainsKey(key))
              {
                  throw new ApplicationException("An ObjectContextBuilder does not exist with a key of " + key);
              }

              context = Storage.GetObjectContextForKey(key);

              if (context == null)
              {
                  context = objectContextBuilders[key].BuildObjectContext();
                  Storage.SetObjectContextForKey(key, context);
              }
          }

          return context;
      }

      /// <summary>
      /// This method is used by application-specific object context storage implementations
      /// and unit tests. Its job is to walk thru existing cached object context(s) and Close() each one.
      /// </summary>
      public static void CloseAllObjectContexts()
      {
          foreach (System.Data.Objects.ObjectContext ctx in Storage.GetAllObjectContexts())
          {
              if (ctx.Connection.State == System.Data.ConnectionState.Open)
                  ctx.Connection.Close();
          }
      }

      private static void AddConfiguration(string connectionStringName, string[] mappingAssemblies, bool recreateDatabaseIfExists = false, bool lazyLoadingEnabled = true)
      {
          if (string.IsNullOrEmpty(connectionStringName))
          {
              throw new ArgumentNullException("connectionStringName");
          }

          if (mappingAssemblies == null)
          {
              throw new ArgumentNullException("mappingAssemblies");
          }

          objectContextBuilders.Add(connectionStringName,
              new ObjectContextBuilder<System.Data.Objects.ObjectContext>(connectionStringName, mappingAssemblies, recreateDatabaseIfExists, lazyLoadingEnabled));
      }        

      /// <summary>
      /// An application-specific implementation of IObjectContextStorage must be setup either thru
      /// <see cref="InitStorage" /> or one of the <see cref="Init" /> overloads. 
      /// </summary>
      private static IObjectContextStorage Storage { get; set; }

      /// <summary>
      /// Maintains a dictionary of object context builders, one per database.  The key is a 
      /// connection string name used to look up the associated database, and used to decorate respective
      /// repositories. If only one database is being used, this dictionary contains a single
      /// factory with a key of <see cref="DefaultConnectionStringName" />.
      /// </summary>
      private static Dictionary<string, IObjectContextBuilder<System.Data.Objects.ObjectContext>> objectContextBuilders = new Dictionary<string, IObjectContextBuilder<System.Data.Objects.ObjectContext>>();

      private static object _syncLock = new object();*/
  }
}