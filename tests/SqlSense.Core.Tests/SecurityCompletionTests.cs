using System.Linq;
using SqlSense.Core.Analysis;
using SqlSense.Core.Completion;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class SecurityCompletionTests
    {
        private readonly CompletionEngine _engine = new CompletionEngine { NewLine = "\n" };

        /// <summary>"|" marks the caret.</summary>
        private CompletionResult Complete(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            return _engine.GetCompletions(text, caret, TestSchema.Create());
        }

        private string Names(string sql) =>
            string.Join(",", Complete(sql).Items.Select(i => i.DisplayText).OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase));

        [Theory]
        [InlineData("ALTER ROLE |", "app_readers,db_datareader")]
        [InlineData("DROP ROLE IF EXISTS |", "app_readers,db_datareader")]
        [InlineData("ALTER ROLE app_readers ADD MEMBER |", @"app_readers,bob,CORP\alice,db_datareader")]
        [InlineData("ALTER ROLE app_readers DROP MEMBER |", @"app_readers,bob,CORP\alice,db_datareader")]
        [InlineData("ALTER SERVER ROLE |", "sysadmin")]
        [InlineData("ALTER SERVER ROLE sysadmin ADD MEMBER |", "bob,old_login,sysadmin")]
        [InlineData("ALTER APPLICATION ROLE |", "reporting_app")]
        [InlineData("ALTER USER |", @"bob,CORP\alice")]
        [InlineData("DROP USER |", @"bob,CORP\alice")]
        [InlineData("ALTER LOGIN |", "bob,old_login")]
        [InlineData("CREATE USER bob FOR LOGIN |", "bob,old_login")]
        [InlineData("ALTER USER bob WITH LOGIN = |", "bob,old_login")]
        [InlineData("GRANT SELECT ON dbo.Customers TO |", @"app_readers,bob,CORP\alice,db_datareader,reporting_app")]
        [InlineData("GRANT SELECT ON dbo.Customers TO bob, |", @"app_readers,bob,CORP\alice,db_datareader,reporting_app")]
        [InlineData("REVOKE EXECUTE ON SCHEMA::sales FROM |", @"app_readers,bob,CORP\alice,db_datareader,reporting_app")]
        [InlineData("GRANT VIEW SERVER STATE TO |", "bob,old_login,sysadmin")]
        [InlineData("ALTER AUTHORIZATION ON SCHEMA::sales TO |", @"app_readers,bob,CORP\alice,db_datareader,reporting_app")]
        [InlineData("CREATE SCHEMA audit AUTHORIZATION |", @"app_readers,bob,CORP\alice,db_datareader,reporting_app")]
        [InlineData("GRANT SELECT ON ROLE::|", "app_readers,db_datareader")]
        [InlineData("ALTER SCHEMA |", "dbo,sales")]
        [InlineData("DROP SCHEMA |", "dbo,sales")]
        [InlineData("GRANT SELECT ON SCHEMA::|", "dbo,sales")]
        [InlineData("ALTER USER bob WITH DEFAULT_SCHEMA = |", "dbo,sales")]
        [InlineData("ALTER DATABASE |", "master,Shop")]
        [InlineData("ALTER LOGIN bob WITH DEFAULT_DATABASE = |", "master,Shop")]
        public void Suggests_existing_principals_schemas_and_databases(string sql, string expected)
        {
            Assert.Equal(expected, Names(sql));
        }

        [Theory]
        [InlineData("CREATE ROLE |")]
        [InlineData("CREATE USER |")]
        [InlineData("CREATE LOGIN |")]
        [InlineData("CREATE SCHEMA |")]
        public void New_names_get_no_suggestions(string sql)
        {
            Assert.Equal(ContextKind.AliasName, Complete(sql).Context.Kind);
            Assert.Empty(Complete(sql).Items);
        }

        [Fact]
        public void Grant_on_lists_securables_but_not_triggers()
        {
            var names = Names("GRANT SELECT ON |").Split(',');
            Assert.Contains("dbo.Customers", names);
            Assert.Contains("dbo.usp_GetOrders", names);
            Assert.Contains("sales", names);
            Assert.DoesNotContain("dbo.trg_Customers_Audit", names);
            Assert.Equal("Customers", Complete("GRANT SELECT ON OBJECT::dbo.Cust|").Items.First().DisplayText);
        }

        [Fact]
        public void Principal_items_are_quoted_and_described()
        {
            var r = Complete("ALTER ROLE app_readers ADD MEMBER |");
            var alice = r.Items.Single(i => i.DisplayText == @"CORP\alice");
            Assert.Equal(@"[CORP\alice]", alice.InsertText);
            Assert.Equal(CompletionItemKind.User, alice.Kind);
            Assert.Equal("database role — members: bob", r.Items.Single(i => i.DisplayText == "app_readers").Description);
            // Own roles before built-in ones.
            Assert.True(r.Items.FindIndex(i => i.DisplayText == "app_readers") < r.Items.FindIndex(i => i.DisplayText == "db_datareader"));
        }

        [Theory]
        [InlineData("SELECT * FROM |", ContextKind.TableSource)]
        [InlineData("ALTER TABLE |", ContextKind.TableSource)]
        [InlineData("ALTER PROCEDURE |", ContextKind.ObjectName)]
        [InlineData("IF EXISTS (SELECT 1 FROM |", ContextKind.TableSource)]
        [InlineData("SELECT USER_NAME(|", ContextKind.Expression)]
        [InlineData("GRANT SELECT ON dbo.Customers TO bob;\nSELECT * FROM |", ContextKind.TableSource)]
        [InlineData("GRANT SELECT ON dbo.Customers TO bob\nSELECT * FROM |", ContextKind.TableSource)]
        public void Other_contexts_are_unchanged(string sql, ContextKind expected)
        {
            Assert.Equal(expected, Complete(sql).Context.Kind);
        }
    }
}
