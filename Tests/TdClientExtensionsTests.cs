using varsender.App;

namespace Tests
{
    public class TdClientExtensionsTests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void TestTrim()
        {
            var inputText = "*\nbegin* Привет!\nПодключайся к *важной* встрече по этой [ссылке](https://t.me/user)!* end\n*";
            var expectedText = "begin Привет!\nПодключайся к важной встрече по этой ссылке! end";

            var formattedText = ParseMarkdown(inputText);
            var actualText = formattedText.Trim();

            Assert.That(actualText.Text, Is.EqualTo(expectedText));
            Assert.That(actualText.Entities.Length, Is.EqualTo(4));
            AssertEntity(actualText.Text, actualText.Entities, 0, "begin");
            AssertEntity(actualText.Text, actualText.Entities, 1, "важной");
            AssertEntity(actualText.Text, actualText.Entities, 2, "ссылке");
            AssertEntity(actualText.Text, actualText.Entities, 3, " end");
        }

        [TestCase("*\n*Привет!\nПодключайся к *важной* встрече по этой [ссылке](https://t.me/user)!*\n*")]
        [TestCase("*\n * Привет!\nПодключайся к *важной* встрече по этой [ссылке](https://t.me/user)! * \n*")]
        public void TestTrimWithEntity(string inputText)
        {
            var expectedText = "Привет!\nПодключайся к важной встрече по этой ссылке!";

            var formattedText = ParseMarkdown(inputText);
            var actualText = formattedText.Trim();

            Assert.That(actualText.Text, Is.EqualTo(expectedText));
            Assert.That(actualText.Entities.Length, Is.EqualTo(2));
            AssertEntity(actualText.Text, actualText.Entities, 0, "важной");
            AssertEntity(actualText.Text, actualText.Entities, 1, "ссылке");
        }

        [Test]
        public void TestCleanupHashtags()
        {
            var inputText = "#header\nПривет!\nПодключайся к *важной* встрече по этой #middle [ссылке](https://t.me/user)!#footer";
            var parsedText = "#header\nПривет!\nПодключайся к важной встрече по этой #middle ссылке!#footer";
            var expectedText = "\nПривет!\nПодключайся к важной встрече по этой  ссылке!";

            var formattedText = ParseMarkdown(inputText);
            AppendHashtag(formattedText, "#header");
            AppendHashtag(formattedText, "#middle");
            AppendHashtag(formattedText, "#footer");

            Assert.That(formattedText.Text, Is.EqualTo(parsedText));
            Assert.That(formattedText.Entities.Length, Is.EqualTo(5));

            var actualText = formattedText.CleanupHashtags();

            Assert.That(actualText.Text, Is.EqualTo(expectedText));
            Assert.That(actualText.Entities.Length, Is.EqualTo(2));

            AssertEntity(actualText.Text, actualText.Entities, 0, "важной");
            AssertEntity(actualText.Text, actualText.Entities, 1, "ссылке");

            void AppendHashtag(TdApi.FormattedText text, string hashtag)
            {
                var entity = new TdApi.TextEntity
                {
                    Type = new TdApi.TextEntityType.TextEntityTypeHashtag(),
                    Offset = text.Text.IndexOf(hashtag),
                    Length = hashtag.Length
                };
                text.Entities = text.Entities.Append(entity).ToArray();
            }
        }

        [Test]
        public void TestInterpolate()
        {
            var inputText = "Привет, {{Имя}}!\nПодключайся к *важной* встрече в {{time}} по этой [ссылке](https://host.com/{{url}})!";
            var variables = new Dictionary<string, string>
            {
                {"имя", "Иван" },
                {"TIME", "14:00" },
                {"url", "https://t.me/user" }
            };
            var expectedText = "Привет, Иван!\nПодключайся к важной встрече в 14:00 по этой ссылке!";

            var formattedText = ParseMarkdown(inputText);
            var actualText = formattedText.Interpolate(variables);

            Assert.That(actualText.Text, Is.EqualTo(expectedText));
            Assert.That(actualText.Entities.Length, Is.EqualTo(2));

            AssertEntity(actualText.Text, actualText.Entities, 0, "важной");
            AssertEntity(actualText.Text, actualText.Entities, 1, "ссылке");
            var textUrlType = actualText.Entities[1].Type as TdApi.TextEntityType.TextEntityTypeTextUrl;
            Assert.IsNotNull(textUrlType);
            Assert.That(textUrlType.Url, Is.EqualTo("https://t.me/user"));
        }

        [TestCase("", "x", "123", ExpectedResult  = "")]
        [TestCase("my perfect text", "", "123", ExpectedResult = "my perfect text")]
        [TestCase("x", "x", "123", ExpectedResult = "123")]
        [TestCase("x ant", "x", "123", ExpectedResult = "123 ant")]
        [TestCase("ant x", "x", "123", ExpectedResult = "ant 123")]
        [TestCase("ant x ant", "x", "123", ExpectedResult = "ant 123 ant")]
        [TestCase("abcxdefxghi", "x", "123", ExpectedResult = "abc123def123ghi")]
        [TestCase("abcxdefXghi", "x", "123", ExpectedResult = "abc123def123ghi")]
        [TestCase("xx", "x", "123", ExpectedResult = "123123")]
        [TestCase("xy", "xy", "123", ExpectedResult = "123")]
        [TestCase("xy ant", "xy", "123", ExpectedResult = "123 ant")]
        [TestCase("ant xy", "xy", "123", ExpectedResult = "ant 123")]
        [TestCase("xyxy", "xy", "123", ExpectedResult = "123123")]
        [TestCase("xyy", "xy", "2x", ExpectedResult = "2xy")]
        public string TestSubstituteOnce(string input, string key, string value)
        {
            var (output, _) = TdClientExtensions.Substitute(input, new TdApi.TextEntity[0], key, value);
            return output;
        }

        [Test]
        public void TestEntityOffsetLonger()
        {
            var input = "my perfect $xy text";
            var expectedText = "my perfect 1234 text";
            var key = "$xy";
            var value = "1234";
            var entities = new[]
            {
                CreateEntity(3, 7), // "perfect"
                CreateEntity(3, 8), // "perfect "
                CreateEntity(3, 10), // "perfect $x"
                CreateEntity(3, 11), // "perfect $xy"
                CreateEntity(3, 12), // "perfect $xy "
                CreateEntity(10, 9), // " $xy text"
                CreateEntity(11, 8), // "$xy text"
                CreateEntity(12, 7), // "xy text"
                CreateEntity(13, 6), // "y text"
                CreateEntity(14, 5), // " text"
                CreateEntity(15, 4), // "text"
                CreateEntity(11, 3), // "$xy"
                CreateEntity(3, 14), // "perfect $xy te"
                CreateEntity(12, 1)  // "x"
            };

            var (actualText, actualEntities) = TdClientExtensions.Substitute(input, entities, key, value);
            Assert.That(actualText, Is.EqualTo(expectedText));

            var i = 0;
            void ApplyedAssert(string expected) => AssertEntity(actualText, actualEntities, i++, expected);

            ApplyedAssert("perfect");
            ApplyedAssert("perfect ");
            ApplyedAssert("perfect 12");
            ApplyedAssert("perfect 1234");
            ApplyedAssert("perfect 1234 ");
            ApplyedAssert(" 1234 text");
            ApplyedAssert("1234 text");
            ApplyedAssert("234 text");
            ApplyedAssert("34 text");
            ApplyedAssert(" text");
            ApplyedAssert("text");
            ApplyedAssert("1234");
            ApplyedAssert("perfect 1234 te");
            ApplyedAssert("2");
        }

        [Test]
        public void TestEntityOffsetShorter()
        {
            var input = "my perfect $xy text";
            var expectedText = "my perfect 1 text";
            var key = "$xy";
            var value = "1";
            var entities = new[]
            {
                CreateEntity(3, 8), // "perfect "
                CreateEntity(3, 10), // "perfect $x"
                CreateEntity(3, 11), // "perfect $xy"
                CreateEntity(11, 8), // "$xy text"
                CreateEntity(12, 7), // "xy text"
                CreateEntity(13, 6), // "y text"
                CreateEntity(14, 5), // " text"
                CreateEntity(11, 3), // "$xy"
                CreateEntity(11, 1), // "$"
                CreateEntity(12, 1), // "x"
                CreateEntity(13, 1) // "y"
            };

            var (actualText, actualEntities) = TdClientExtensions.Substitute(input, entities, key, value);
            Assert.That(actualText, Is.EqualTo(expectedText));

            var i = 0;
            void ApplyedAssert(string expected) => AssertEntity(actualText, actualEntities!, i++, expected);
            ApplyedAssert("perfect ");
            ApplyedAssert("perfect 1");
            ApplyedAssert("perfect 1");
            ApplyedAssert("1 text");
            ApplyedAssert(" text");
            ApplyedAssert(" text");
            ApplyedAssert(" text");
            ApplyedAssert("1");
            ApplyedAssert("1");
            ApplyedAssert("");
            ApplyedAssert("");
        }

        [Test]
        // несколько сущностей (ниже приведены опасные условия, т.к. offset уже может быть в рамках новой строки)
        //  if (entity.Offset >= index + key.Length)
        //  if (entity.Offset + entity.Length >= index + key.Length)
        public void TestEntityOffsetSeveral()
        {
            var input = "$xybold$xy";
            var expectedText = "1234567890bold1234567890";
            var key = "$xy";
            var value = "1234567890";
            var entities = new[]
            {
                CreateEntity(3, 4) // "bold"
            };

            var (actualText, actualEntities) = TdClientExtensions.Substitute(input, entities, key, value);
            Assert.That(actualText, Is.EqualTo(expectedText));

            var i = 0;
            void ApplyedAssert(string expected) => AssertEntity(actualText, actualEntities, i++, expected);

            ApplyedAssert("bold");
        }

        private static TdApi.FormattedText ParseMarkdown(string input) =>
            new TdClient().ParseTextEntitiesAsync(input,
                new TdApi.TextParseMode.TextParseModeMarkdown()).Result;

        private static void AssertEntity(string text, TdApi.TextEntity[] entities, int index, string expected)
        {
            if (index >= entities.Length)
                Assert.Fail();
            var entity = entities[index];

            var actual = text.Substring(entity.Offset, entity.Length);
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static TdApi.TextEntity CreateEntity(int offset, int length) =>
            new TdApi.TextEntity
            {
                Offset = offset,
                Length = length,
            };
    }
}