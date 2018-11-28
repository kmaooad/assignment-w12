using System;
using SutStartup = KmaOoad18.Leanware.Web.Startup;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using KmaOoad18.Leanware.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using System.Net;
using System.IO;

namespace KmaOoad18.Leanware.Web
{
    public class Spec : IClassFixture<WebApplicationFactory<SutStartup>>
    {
        private readonly WebApplicationFactory<SutStartup> _factory;

        public Spec(WebApplicationFactory<SutStartup> factory)
        {
            File.Delete("test.db");

            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSolutionRelativeContentRoot("./Leanware.Web/");

                builder.ConfigureServices(services =>
                {
                    services.AddDbContext<LeanwareContext>(options =>
                    options.UseSqlite("Data Source=test.db"));

                    var serviceProvider = services.BuildServiceProvider();

                    using (var scope = serviceProvider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<LeanwareContext>();
                        db.Database.Migrate();
                    }
                });
            });
        }

        [Fact]
        public async Task CanRenameTags()
        {
            var client = new LeanwareTestClient(_factory);

            var feature = (await client.Create<Feature>()).Received;

            var tag = feature.Tags.First();
            var newTag = string.Join("", tag.Reverse());

            await client.Post(path: $"api/tags/{tag}/renameTo/{newTag}");

            feature = await client.Get<Feature>(feature.ReadPath);
            feature.Tags.Should().Contain(newTag);
        }

        [Fact]
        public async Task CanDeleteTags()
        {
            var client = new LeanwareTestClient(_factory);

            var feature = (await client.Create<Feature>()).Received;

            var tag = feature.Tags.First();

            await client.Delete($"api/tags/{tag}");

            feature = await client.Get<Feature>(feature.ReadPath);
            feature.Tags.Should().NotContain(tag);
        }

        [Fact]
        public async Task CanImplementStories()
        {
            var client = new LeanwareTestClient(_factory);

            var feature = (await client.Create<Feature>()).Received;
            var stories = new List<Story>();

            for (int i = 0; i < 5; i++)
            {
                var s = (await client.Create<Story>(new { FeatureId = feature.Id })).Received;
                stories.Add(s);
            }

            var response = await client.Post(path: $"api/stories/{stories[0].Id}/start");
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "only approved stories can be implemented");

            await client.Post(path: $"api/features/{feature.Id}/approve");

            response = await client.Post(path: $"api/stories/{stories[0].Id}/finish");
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "story cannot be finished without being started first");

            response = await client.Post(path: $"api/stories/{stories[0].Id}/start");

            response.EnsureSuccessStatusCode();

            feature = await client.Get<Feature>(feature.ReadPath);
            feature.Status.Should().Be("Implementing");

            var story = await client.Get<Story>(stories[0].ReadPath);
            story.Status.Should().Be("Implementing");

            response = await client.Post(path: $"api/stories/{story.Id}/finish");
            response.EnsureSuccessStatusCode();

            feature = await client.Get<Feature>(feature.ReadPath);
            feature.Status.Should().Be("Implementing");

            story = await client.Get<Story>(story.ReadPath);
            story.Status.Should().Be("Implemented");

            for (int i = 1; i < stories.Count; i++)
            {
                await client.Post(path: $"api/stories/{stories[i].Id}/start");
                await client.Post(path: $"api/stories/{stories[i].Id}/finish");
            }

            feature = await client.Get<Feature>(feature.ReadPath);
            feature.Status.Should().Be("Implemented", "when all stories are implemented, feature is automatically set to implemented too");

            var additionalStory = (await client.Create<Story>(new { FeatureId = feature.Id })).Received;

            additionalStory.Status.Should().Be("Approved", "when new story is added to implemented feature, it is considered approved by default");

            feature = await client.Get<Feature>(feature.ReadPath);
            feature.Status.Should().Be("ChangeRequested");
        }

        [Fact]
        public async Task CanFilterFeaturesByTags()
        {
            var client = new LeanwareTestClient(_factory);

            var testTag = $"{Guid.NewGuid().ToString().Split('-')[1]}{Guid.NewGuid().ToString().Split('-')[1]}";

            var features = new List<Feature>();
            var taggedFeatures = new List<Feature>();

            for (int i = 0; i < 10; i++)
            {
                features.Add((await client.Create<Feature>()).Received);
            }

            for (int i = 0; i < 5; i++)
            {
                var tag = features[i].Tags.First();

                taggedFeatures.Add(features[i]);

                await client.Update<Feature>(features[i].UpdatePath, new { Tags = new List<string> { testTag } });
            }

            var filteredFeatures = await client.Get<List<Feature>>($"api/features?tag={testTag}");

            filteredFeatures.Select(ff => ff.Id).Should().BeEquivalentTo(taggedFeatures.Select(tf => tf.Id));
        }

        [Fact]
        public async Task CanApproveNewFeatures()
        {
            var client = new LeanwareTestClient(_factory);
            var feature = (await client.Create<Feature>()).Received;
            var stories = new List<Story>();

            for (int i = 0; i < 5; i++)
            {
                var story = (await client.Create<Story>(new { FeatureId = feature.Id })).Received;
                stories.Add(story);
            }

            var response = await client.Post(path: $"api/features/{feature.Id}/approve");

            response.EnsureSuccessStatusCode();

            feature = await client.Get<Feature>(feature.ReadPath);
            feature.Status.Should().Be("Approved");

            foreach (var s in stories)
            {
                var story = await client.Get<Story>(s.ReadPath);
                story.Status.Should().Be("Approved");
            }

            // Only New feature can be approved, otherwise HTTP 400 BadRequest should be returned
            response = await client.Post(path: $"api/features/{feature.Id}/approve");
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task CannotChangeFeatureStatus()
        {
            var client = new LeanwareTestClient(_factory);

            var feature = (await client.Create<Feature>()).Received;

            var directUpdate = new { Status = "Approved" };

            var updateResponse = await client.Patch(feature.UpdatePath, directUpdate);

            feature = await client.Get<Feature>(feature.ReadPath);

            feature.Status.Should().Be("New");
        }

        [Fact]
        public async Task CanArrangeFeaturesByPriority()
        {
            var client = new LeanwareTestClient(_factory);

            var approvedFeatures = new List<Feature>();
            var inProgressFeatures = new List<Feature>();


            for (int i = 0; i < 10; i++)
            {
                var stories = new List<Story>();

                var feature = (await client.Create<Feature>()).Received;

                for (int k = 0; k < 5; k++)
                {
                    var story = (await client.Create<Story>(new { FeatureId = feature.Id })).Received;
                    stories.Add(story);
                }

                await client.Post(path: $"api/features/{feature.Id}/approve");

                if (i > 4)
                {
                    await client.Post(path: $"api/stories/{stories.First().Id}/start");
                    inProgressFeatures.Add(feature);
                }
                else
                {
                    approvedFeatures.Add(feature);
                }
            }

            await client.Post(path: $"api/features/{approvedFeatures.First().Id}/position/3");

            var assertedIds = new[] { 1, 2, 0, 3, 4 }.Select(ix => approvedFeatures[ix].Id).ToList();

            var approvedStack = await client.Get<List<Feature>>(path: $"api/features/stack/approved");
            var rearrangedIds = approvedStack.Select(sf => sf.Id).ToList();

            rearrangedIds.Should().BeEquivalentTo(assertedIds);

            var inProgressIds = inProgressFeatures.Select(af => af.Id).ToList();
            var inProgressStack = await client.Get<List<Feature>>(path: $"api/features/stack/implementing");
            var notAffectedIds = inProgressStack.Select(sf => sf.Id).ToList();
            notAffectedIds.Should().BeEquivalentTo(inProgressIds);
        }

        [Fact]
        public async Task CanCrudStoriesAndFeatures()
        {
            // Given CRUD client
            var client = new LeanwareTestClient(_factory);

            // When I create new feature
            var createFeatureResult = await client.Create<Feature>();

            // Then created feature should have valid ID and title
            var createdFeature = createFeatureResult.Received;
            createdFeature.Id.Should().BeGreaterThan(0);
            createdFeature.Status.Should().Be("New");
            createdFeature.Title.Should().Be(createFeatureResult.Sent.Title);

            // When I update feature
            var featureUpdate = new { Tags = RandomTags };

            // Then updated feature should have new title
            var featureUpdateResult = await client.Update<Feature>(createdFeature.UpdatePath, featureUpdate);

            featureUpdateResult.Received.Title.Should().Be(createdFeature.Title);
            featureUpdateResult.Received.Tags.Should().Contain(featureUpdate.Tags);

            // When I create story
            var createStoryResult = await client.Create<Story>(new { FeatureId = createdFeature.Id });

            // Then I created story should have valid ID and title
            var createdStory = createStoryResult.Received;

            createdStory.Id.Should().BeGreaterThan(0);

            createdStory.Title.Should().Be(createStoryResult.Sent.Title);

            createdStory.Status.Should().Be("New");

            createdStory.FeatureId.Should().Be(createdFeature.Id);

            // When I update story
            var storyUpdate = new { Title = RandomTitle("S") };

            // Then updated story should have new title
            var storyUpdateResult = await client.Update<Story>(createdStory.UpdatePath, storyUpdate);

            storyUpdateResult.Received.Title.Should().Be(storyUpdate.Title);

            // When I delete story
            await client.Delete(createdStory.DeletePath);

            // Then getting story should return 404
            var deletedStory = await client.Get(createdStory.ReadPath);
            deletedStory.StatusCode.Should().Be(HttpStatusCode.NotFound);

            // When I delete feature
            await client.Delete(createdFeature.DeletePath);

            // Then getting feature should return 404
            var deletedFeature = await client.Get(createdFeature.ReadPath);
            deletedFeature.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task CanEnforceWipLimit()
        {
            const int wipLimit = 5;

            var client = new LeanwareTestClient(_factory);

            var stories = new List<Story>();

            for (int i = 0; i < wipLimit + 1; i++)
            {
                var feature = (await client.Create<Feature>()).Received;
                var story = (await client.Create<Story>(new { FeatureId = feature.Id })).Received;
                stories.Add(story);
                await client.Post(path: $"api/features/{feature.Id}/approve");
            }

            for (int i = 0; i < wipLimit; i++)
            {
                await client.Post(path: $"api/stories/{stories[i].Id}/start");
                var story = await client.Get<Story>(stories[0].ReadPath);
                story.Status.Should().Be("Implementing");
            }

            var response = await client.Post(path: $"api/stories/{stories[wipLimit].Id}/start");
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"only {wipLimit} features can be in progress at the same time");
        }

        [Fact]
        public async Task CanCollectLogs()
        {
            var client = new LeanwareTestClient(_factory);

            var feature = (await client.Create<Feature>()).Received;

            var story = (await client.Create<Story>(new { FeatureId = feature.Id })).Received;

            await client.Post(path: $"api/features/{feature.Id}/approve");

            await client.Post(path: $"api/stories/{story.Id}/start");

            await client.Post(path: $"api/stories/{story.Id}/finish");

            var storyUpdate = new { Title = RandomTitle("S") };

            await client.Update<Story>(story.UpdatePath, storyUpdate);

            var logs = await client.Get<List<string>>(path: $"api/logs");

            var assertedLogs = new[] {
                $"Feature #{feature.Id} has been created",
                $"Story #{story.Id} has been added to feature #{feature.Id}",
                $"Feature #{feature.Id} has been approved",
                $"Story #{story.Id} has been started",
                $"Story #{story.Id} has been finished",
                $"Feature #{feature.Id} has been finished",
                $"Story #{story.Id} has been updated"
            };

            foreach (var al in assertedLogs)
            {
                logs.Should().Contain(al);
            }
        }

        static List<string> RandomTags => Guid.NewGuid().ToString().Split('-').ToList();

        static string RandomTitle(string prefix) => $"{prefix}{DateTime.Now.Ticks}";

        static string RandomText => "Random text";



        #region DTOs

        class CreateResult<T> where T : IApiDto
        {
            public T Sent { get; }
            public T Received { get; }

            public CreateResult(T sent, T received) { Sent = sent; Received = received; }
        }

        class UpdateResult<T> where T : IApiDto
        {
            public T Received { get; }

            public UpdateResult(T received) { Received = received; }
        }

        interface IApiDto
        {
            void Generate(dynamic externalDeps = null);

            string BasePath { get; }
            string CreatePath { get; }
            string ReadPath { get; }
            string UpdatePath { get; }
            string DeletePath { get; }
        }

        class Story : IApiDto
        {

            public string BasePath => "api/stories";
            public string ReadPath => $"{BasePath}/{Id}";
            public string UpdatePath => ReadPath;
            public string DeletePath => ReadPath;
            public string CreatePath => BasePath;

            public Story() { }

            public int Id { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public List<string> Tags { get; set; }
            public int FeatureId { get; set; }
            public string Status { get; set; }

            public void Generate(dynamic externalDeps)
            {
                var storyTitle = RandomTitle("St");

                Id = 0;
                Title = storyTitle;
                Description = RandomText;
                Tags = RandomTags;
                FeatureId = externalDeps.FeatureId;
            }
        }

        class Feature : IApiDto
        {

            public string BasePath => "api/features";
            public string ReadPath => $"{BasePath}/{Id}";
            public string UpdatePath => ReadPath;
            public string DeletePath => ReadPath;
            public string CreatePath => BasePath;

            public int Id { get; set; }
            public string Title { get; set; }
            public List<string> Tags { get; set; }
            public string Status { get; set; }

            public void Generate(dynamic externalDeps = null)
            {
                var featureTitle = RandomTitle("F");

                Id = 0;
                Title = featureTitle;
                Tags = RandomTags;
            }
        }

        #endregion



        #region Client 
        private class LeanwareTestClient
        {
            private readonly HttpClient _client;

            public LeanwareTestClient(WebApplicationFactory<SutStartup> factory)
            {
                this._client = factory.CreateClient();
            }


            internal async Task<CreateResult<T>> Create<T>(dynamic externalDeps = null) where T : class, IApiDto, new()
            {
                var sent = new T();

                sent.Generate(externalDeps);

                var received = await PostSelf(sent);

                return new CreateResult<T>(sent, received);
            }

            internal async Task<UpdateResult<T>> Update<T>(string path, dynamic dto) where T : class, IApiDto, new()
            {
                await Patch(path, dto);

                var received = await Get<T>(path);

                return new UpdateResult<T>(received);
            }

            internal async Task<HttpResponseMessage> Post(string path, dynamic dto = null)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, path);

                if (dto != null)
                {
                    request.Content = new StringContent(JsonConvert.SerializeObject(dto), Encoding.UTF8, "application/json");
                }

                return await _client.SendAsync(request);
            }

            internal async Task<HttpResponseMessage> Post<T>(T dto) where T : IApiDto
            {
                var response = await Post(dto.CreatePath, dto);

                response.EnsureSuccessStatusCode();

                return response;
            }

            internal async Task<T> PostSelf<T>(T dto) where T : IApiDto
            {
                var response = await Post(dto);

                var content = await response.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeObject<T>(content);
            }

            internal async Task<HttpResponseMessage> Patch(string path, dynamic dto)
            {
                var request = new HttpRequestMessage(HttpMethod.Patch, path);

                request.Content = new StringContent(JsonConvert.SerializeObject(dto), Encoding.UTF8, "application/json");

                var response = await _client.SendAsync(request);

                response.EnsureSuccessStatusCode();

                return response;
            }

            internal async Task<HttpResponseMessage> Delete(string path)
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, path);

                var response = await _client.SendAsync(request);

                response.EnsureSuccessStatusCode();

                return response;
            }

            internal async Task<T> Get<T>(string path) where T : new()
            {
                var request = new HttpRequestMessage(HttpMethod.Get, path);

                var response = await _client.SendAsync(request);

                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeAnonymousType(responseContent, new T());
            }

            internal async Task<HttpResponseMessage> Get(string path)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, path);

                var response = await _client.SendAsync(request);

                return response;
            }
        }
        #endregion
    }
}