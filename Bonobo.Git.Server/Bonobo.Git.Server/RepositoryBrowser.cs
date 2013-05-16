using Bonobo.Git.Server.Models;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonobo.Git.Server
{
    public class RepositoryBrowser : IDisposable
    {
        private Repository _repository;
        private bool _isDisposed;

        public RepositoryBrowser(string repositoryPath)
        {
            _repository = new Repository(repositoryPath);
        }

        public IEnumerable<string> GetBranches()
        {
            return _repository.Branches.Select(s => s.Name).ToList();
        }

        public IEnumerable<RepositoryCommitModel> GetCommits(string name, out string branchName)
        {
            branchName = "";

            var result = new List<RepositoryCommitModel>();

            Branch branch;
            Commit commit = GetCommitByName(name, out branch);

            if (commit == null)
                return result;
            if (branch != null)
                branchName = branch.Name;

            var ancestors = _repository.Commits.QueryBy(new Filter { Since = commit, SortBy = GitSortOptions.Topological });
            result.AddRange(ancestors.Select(s => ConvertToRepositoryCommitModel(s)));

            return result;
        }

        public RepositoryCommitModel GetCommitDetail(string id)
        {
            var commit = _repository.Commits.FirstOrDefault(s => s.Sha == id);
            if (commit == null)
                return null;
            else
                return ConvertToRepositoryCommitModel(commit, true);
        }

        public IEnumerable<RepositoryTreeDetailModel> BrowseTree(string name, string path, out string branchName)
        {
            branchName = null;
            if (path == null)
                path = "";

            var result = new List<RepositoryTreeDetailModel>();

            Branch branch;
            Commit commit = GetCommitByName(name, out branch);

            if (commit == null)
                return result;
            if (branch != null)
                branchName = branch.Name;

            var branchNameTemp = branchName;
            var ancestors = _repository.Commits.QueryBy(new Filter { Since = commit, SortBy = GitSortOptions.Topological | GitSortOptions.Reverse });
            var q = from item in string.IsNullOrEmpty(path) ? commit.Tree : (Tree)commit[path].Target
                    let lastCommit = ancestors.First(c =>
                    {
                        var entry = c[item.Path];
                        return entry != null && entry.Target == item.Target;
                    })
                    select new RepositoryTreeDetailModel
                    {
                        Name = item.Name,
                        IsTree = item.TargetType == TreeEntryTargetType.Tree,
                        CommitDate = lastCommit.Author.When.LocalDateTime,
                        CommitMessage = lastCommit.MessageShort,
                        Author = lastCommit.Author.Name,
                        TreeName = branchNameTemp ?? name,
                        Path = item.Path.Replace('\\', '/'),
                    };
            return q.ToList();
        }

        public RepositoryTreeDetailModel BrowseBlob(string name, string path, out string branchName)
        {
            branchName = null;
            if (path == null)
                path = "";

            Branch branch;
            Commit commit = GetCommitByName(name, out branch);

            if (commit == null)
                return null;
            if (branch != null)
                branchName = branch.Name;

            var tree = commit.Tree;
            var dirs = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            TreeEntry entry;
            foreach (var dir in dirs.Take(dirs.Length - 1))
            {
                entry = tree.FirstOrDefault(s => s.TargetType == TreeEntryTargetType.Tree && s.Name == dir);

                if (entry == null)
                    return null;
                tree = (Tree)entry.Target;
            }
            entry = tree.FirstOrDefault(s => s.TargetType == TreeEntryTargetType.Blob && s.Name == dirs.Last());
            if (entry == null)
                return null;
            var blob = (Blob)entry.Target;

            return new RepositoryTreeDetailModel
            {
                Name = dirs.Last(),
                IsTree = false,
                CommitDate = commit.Author.When.LocalDateTime,
                CommitMessage = commit.Message,
                Author = commit.Author.Name,
                TreeName = branchName ?? name,
                Path = path,
                Data = blob.Content,
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            try
            {
                if (!this._isDisposed)
                {
                    if (isDisposing)
                    {
                        if (_repository != null)
                        {
                            _repository.Dispose();
                        }
                    }
                }
            }
            finally
            {
                this._isDisposed = true;
            }
        }

        private Commit GetCommitByName(string name, out Branch branch)
        {
            Commit commit = null;
            branch = string.IsNullOrEmpty(name) ? _repository.Head : _repository.Branches[name];
            if (branch != null && branch.Tip != null)
                commit = branch.Tip;

            if (commit == null && name != null)
            {
                var tag = _repository.Tags[name];
                if (tag != null)
                    name = tag.Target.Sha;

                commit = _repository.Commits.FirstOrDefault(s => s.Sha == name);
            }
            return commit;
        }

        private RepositoryCommitModel ConvertToRepositoryCommitModel(Commit commit, bool withDiff = false)
        {
            var model = new RepositoryCommitModel
            {
                Author = commit.Author.Name,
                AuthorEmail = commit.Author.Email,
                Date = commit.Author.When.LocalDateTime,
                ID = commit.Sha,
                Message = commit.MessageShort,
                TreeID = commit.Tree.Sha,
                Parents = commit.Parents.Select(i => i.Sha).ToArray(),
            };
            if (withDiff)
            {
                TreeChanges changes = commit.Parents.Count() == 0
                    ? _repository.Diff.Compare(null, commit.Tree)
                    : _repository.Diff.Compare(commit.Parents.First().Tree, commit.Tree);
                model.Changes = changes.OrderBy(s => s.Path).Select(i => new RepositoryCommitChangeModel
                {
                    //Name = i.Name,
                    Path = i.Path,
                    Status = i.Status,
                });
            }
            return model;
        }
    }
}