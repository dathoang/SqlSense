using System.Linq;
using SqlSense.Core.Analysis;
using SqlSense.Core.Completion;
using SqlSense.Core.Schema;
using Xunit;

namespace SqlSense.Core.Tests
{
    public class AlterObjectTests
    {
        private readonly CompletionEngine _engine = new CompletionEngine { NewLine = "\n" };

        /// <summary>"|" marks the caret.</summary>
        private CompletionResult Complete(string textWithCaret)
        {
            var caret = textWithCaret.IndexOf('|');
            var text = textWithCaret.Remove(caret, 1);
            return _engine.GetCompletions(text, caret, TestSchema.Create());
        }

        [Theory]
        [InlineData("ALTER PROCEDURE |", "dbo.usp_GetOrders")]
        [InlineData("alter proc |", "dbo.usp_GetOrders")]
        [InlineData("CREATE OR ALTER PROCEDURE |", "dbo.usp_GetOrders")]
        [InlineData("DROP PROCEDURE |", "dbo.usp_GetOrders")]
        [InlineData("DROP PROCEDURE IF EXISTS |", "dbo.usp_GetOrders")]
        [InlineData("ALTER FUNCTION |", "dbo.fn_OrdersOf,dbo.fn_Tax")]
        [InlineData("ALTER VIEW |", "dbo.ActiveCustomers")]
        [InlineData("ALTER TRIGGER |", "dbo.trg_Customers_Audit")]
        [InlineData("DROP TRIGGER |", "dbo.trg_Customers_Audit")]
        [InlineData("ALTER FUNCTION dbo.|", "fn_OrdersOf,fn_Tax")]
        public void Lists_only_objects_of_that_kind(string sql, string expected)
        {
            var r = Complete(sql);
            Assert.Equal(ContextKind.ObjectName, r.Context.Kind);
            var objects = r.Items.Where(i => i.Kind != CompletionItemKind.Schema).Select(i => i.DisplayText).OrderBy(x => x);
            Assert.Equal(expected, string.Join(",", objects));
        }

        [Fact]
        public void Function_names_have_no_parentheses()
        {
            var item = Complete("ALTER FUNCTION fn_t|").Items.First();
            Assert.Equal("dbo.fn_Tax", item.InsertText);
            Assert.Equal(-1, item.CaretOffset);
        }

        [Theory]
        [InlineData("ALTER PROCEDURE |", "ALTER", 0)]
        [InlineData("SELECT 1\nGO\nCREATE OR ALTER PROC usp|", "CREATE OR ALTER", 12)]
        [InlineData("-- fix\nALTER VIEW dbo.Act|\n", "ALTER", 7)]
        public void Alter_in_an_empty_batch_requests_the_definition(string sql, string verb, int statementStart)
        {
            var item = Complete(sql).Items.First(i => i.Kind != CompletionItemKind.Schema);
            Assert.NotNull(item.ScriptRequest);
            Assert.Equal(verb, item.ScriptRequest.Verb);
            Assert.Equal(statementStart, item.ScriptRequest.StatementStart);
            Assert.EndsWith(item.DisplayText, item.ScriptRequest.Object.QualifiedName);
        }

        [Theory]
        [InlineData("DROP PROCEDURE |")]
        [InlineData("ALTER PROCEDURE |\nAS\nSELECT 1")]
        [InlineData("ALTER PROCEDURE dbo.usp_Get|Orders @x int AS SELECT 1")]
        public void No_definition_when_dropping_or_the_body_is_there(string sql)
        {
            Assert.All(Complete(sql).Items, i => Assert.Null(i.ScriptRequest));
        }

        [Fact]
        public void Definition_is_optional()
        {
            var engine = new CompletionEngine(new CompletionOptions { AlterScriptsDefinition = false });
            var r = engine.GetCompletions("ALTER PROCEDURE ", 16, TestSchema.Create());
            Assert.All(r.Items, i => Assert.Null(i.ScriptRequest));
        }

        [Theory]
        [InlineData("CREATE PROCEDURE |", ContextKind.AliasName)]
        [InlineData("CREATE TRIGGER trg ON |", ContextKind.TableSource)]
        [InlineData("ALTER TABLE |", ContextKind.TableSource)]
        [InlineData("DROP TABLE IF EXISTS |", ContextKind.TableSource)]
        public void Related_contexts(string sql, ContextKind expected)
        {
            Assert.Equal(expected, Complete(sql).Context.Kind);
        }

        [Fact]
        public void Triggers_are_not_table_sources()
        {
            Assert.DoesNotContain(Complete("SELECT * FROM dbo.|").Items, i => i.DisplayText == "trg_Customers_Audit");
            Assert.DoesNotContain(Complete("SELECT * FROM |").Items, i => i.Kind == CompletionItemKind.Trigger);
        }

        [Theory]
        [InlineData("CREATE PROCEDURE dbo.p\nAS\nSELECT 1;\n", "ALTER", "ALTER PROCEDURE dbo.p\nAS\nSELECT 1;")]
        [InlineData("/* header */\r\ncreate proc p as select 1", "ALTER", "/* header */\r\nalter proc p as select 1")]
        [InlineData("CREATE OR ALTER VIEW v AS SELECT 1 AS a", "ALTER", "ALTER VIEW v AS SELECT 1 AS a")]
        [InlineData("CREATE VIEW v AS SELECT 'CREATE' AS a", "CREATE OR ALTER", "CREATE OR ALTER VIEW v AS SELECT 'CREATE' AS a")]
        [InlineData("-- create\nCREATE TRIGGER t ON x AFTER INSERT AS SET NOCOUNT ON", "ALTER", "-- create\nALTER TRIGGER t ON x AFTER INSERT AS SET NOCOUNT ON")]
        public void Builds_alter_script(string definition, string verb, string expected)
        {
            Assert.Equal(expected, ModuleScript.Build(definition, verb));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("SELECT 1")]
        public void No_script_without_a_create(string definition)
        {
            Assert.Null(ModuleScript.Build(definition, "ALTER"));
        }
    }
}
