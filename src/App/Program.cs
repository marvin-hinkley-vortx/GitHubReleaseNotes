namespace ReleaseNotesCompiler.CLI
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using CommandLine;
    using CommandLine.Text;
    using Octokit;
    using FileMode = System.IO.FileMode;

    abstract class CommonSubOptions
    {
        [Option('t', "accesstoken", HelpText = "The API token to access GitHub with.", Required = true)]
        public string ApiToken { get; set; }

        [Option('o', "owner", HelpText = "The owner of the repository.", Required = true)]
        public string RepositoryOwner { get; set; }

        [Option('r', "repository", HelpText = "The name of the repository.", Required = true)]
        public string RepositoryName { get; set; }

        [Option('m', "milestone", HelpText = "The milestone to use.", Required = true)]
        public string Milestone { get; set; }
        
        [Option('f', "outputfile", HelpText = "The file to write release notes to.", Required = true)]
        public string OutputFile { get; set; }
        
        [Option('c', "createfile", HelpText = "Will create release notes file rather than a release.", Required = false)]
        public bool CreateFile { get; set; }

        public GitHubClient CreateGitHubClient()
        {
            var creds = new Credentials(ApiToken);
            var github = new GitHubClient(new ProductHeaderValue("ReleaseNotesCompiler")) { Credentials = creds };

            return github;
        }
    }

    class CreateSubOptions : CommonSubOptions
    {
        [Option('a', "asset", HelpText = "Path to the file to include in the release.", Required = false)]
        public string AssetPath { get; set; }

        [Option('t', "targetcommitish", HelpText = "The commit to tag. Can be a branch or SHA. Defaults to repo's default branch.", Required = false)]
        public string TargetCommitish { get; set; }
    }

    class AttachSubOptions : CommonSubOptions
    {
        [Option('a', "asset", HelpText = "Path to the file to include in the release.", Required = false)]
        public string AssetPath { get; set; }
    }

    class PublishSubOptions : CommonSubOptions
    {
    }

    class Options
    {
        [VerbOption("create", HelpText = "Creates a draft release notes from a milestone.")]
        public CreateSubOptions CreateVerb { get; set; }

        [VerbOption("attach", HelpText = "Attaches an asset to a release.")]
        public AttachSubOptions AttachVerb { get; set; }

        [VerbOption("publish", HelpText = "Publishes the release notes and closes the milestone.")]
        public PublishSubOptions PublishVerb { get; set; }

        [HelpVerbOption]
        public string DoHelpForVerb(string verbName)
        {
            return HelpText.AutoBuild(this, verbName);
        }
    }

    class Program
    {
        static int Main(string[] args)
        {
            var options = new Options();

            var result = 1;

            if (!Parser.Default.ParseArgumentsStrict(args, options, (verb, subOptions) =>
                {
                    if (verb == "create")
                    {
                        result = CreateReleaseAsync((CreateSubOptions)subOptions).Result;
                    }

                    if (verb == "attach")
                    {
                        result = AttachToReleaseAsync((AttachSubOptions)subOptions).Result;
                    }

                    if (verb == "publish")
                    {
                        result = PublishReleaseAsync((PublishSubOptions)subOptions).Result;
                    }
                }))
            {
                return 1;
            }

            return result;
        }

        static async Task<int> CreateReleaseAsync(CreateSubOptions options)
        {
            try
            {
                var github = options.CreateGitHubClient();

                await CreateRelease(
                    github,
                    options.RepositoryOwner,
                    options.RepositoryName,
                    options.Milestone,
                    options.TargetCommitish,
                    options.AssetPath,
                    options.OutputFile,
                    options.CreateFile
                );

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                return 1;
            }
        }

        static async Task<int> AttachToReleaseAsync(AttachSubOptions options)
        {
            try
            {
                var github = options.CreateGitHubClient();

                await AttachToRelease(github, options.RepositoryOwner, options.RepositoryName, options.Milestone, options.AssetPath);

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                return 1;
            }
        }

        static async Task<int> PublishReleaseAsync(PublishSubOptions options)
        {
            try
            {
                var github = options.CreateGitHubClient();

                await CloseMilestone(github, options.RepositoryOwner, options.RepositoryName, options.Milestone);

                await PublishRelease(github, options.RepositoryOwner, options.RepositoryName, options.Milestone);

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                return 1;
            }
        }

        static async Task CreateRelease(GitHubClient github, string owner, string repository, string milestone, string targetCommitish, string asset, string outputFile, bool createFile)
        {
            var releaseNotesBuilder = new ReleaseNotesBuilder(new DefaultGitHubClient(github, owner, repository), owner, repository, milestone);
            var result = await releaseNotesBuilder.BuildReleaseNotes();

            if(createFile)
            {
                CreateReleaseNotesFile(result, outputFile);
            }
            else
            {
                var releaseUpdate = new NewRelease(milestone)
                {
                    Draft = true,
                    Body = result,
                    Name = milestone,
                };
                if (!string.IsNullOrEmpty(targetCommitish))
                    releaseUpdate.TargetCommitish = targetCommitish;

                var release = await github.Repository.Release.Create(owner, repository, releaseUpdate);
                if (File.Exists(asset))
                {
                    var upload = new ReleaseAssetUpload { FileName = Path.GetFileName(asset), ContentType = "application/octet-stream", RawData = File.Open(asset, FileMode.Open) };
                    await github.Repository.Release.UploadAsset(release, upload);
                }
            }
        }

        static void CreateReleaseNotesFile(string releaseNotes, string outputFile)
        {
            using(var writer = new StreamWriter(outputFile))
            {
                writer.Write(releaseNotes);
            }
        } 
        
        static async Task AttachToRelease(GitHubClient github, string owner, string repository, string milestone, string asset)
        {
            if (!File.Exists(asset))
                return;

            var releases = await github.Repository.Release.GetAll(owner, repository);
            var release = releases.FirstOrDefault(r => r.Name == milestone);
            if (release == null)
                return;

            var upload = new ReleaseAssetUpload { FileName = Path.GetFileName(asset), ContentType = "application/octet-stream", RawData = File.Open(asset, FileMode.Open) };

            await github.Repository.Release.UploadAsset(release, upload);
        }

        static async Task CloseMilestone(GitHubClient github, string owner, string repository, string milestoneTitle)
        {
            var milestoneClient = github.Issue.Milestone;
            var openMilestones = await milestoneClient.GetAllForRepository(owner, repository, new MilestoneRequest { State = ItemStateFilter.Open });
            var milestone = openMilestones.FirstOrDefault(m => m.Title == milestoneTitle);
            if (milestone == null)
                return;

            await milestoneClient.Update(owner, repository, milestone.Number, new MilestoneUpdate { State = ItemState.Closed });
        }

        static async Task PublishRelease(GitHubClient github, string owner, string repository, string milestone)
        {
            var releases = await github.Repository.Release.GetAll(owner, repository);
            var release = releases.FirstOrDefault(r => r.Name == milestone);
            if (release == null)
                return;

            var releaseUpdate = new ReleaseUpdate
            {
                Draft = false
            };

            await github.Repository.Release.Edit(owner, repository, release.Id, releaseUpdate);
        }
    }
}