# How to contribute

We are really glad you're reading this, because we look forward to developers helping this project come to fruition. The best way to contribute is to find something that you are excited about and work on it. If you have an idea for a feature, please open an issue and describe your idea or provide a working prototype. If you want to work on an existing issue, please comment on the issue and let us know that you are working on it.

## Testing

We have a handful of XUnit unit tests, most of them are visual tests that compare hashed screenshots. Please write unit tests for new code you create.

## Submitting changes

Please send a [GitHub Pull Request to DNMG](https://github.com/twobrainsgmbh/dnmg/pull/new/master) with a clear list of what you've done (read more about [pull requests](http://help.github.com/pull-requests/)). When you send a pull request, we will love you forever if you include (visual) unit tests. We can always use more test coverage. Please follow our coding conventions (below) and make sure all of your commits are atomic (one feature per commit).

Always write a clear log message for your commits, use [conventional commits](https://www.conventionalcommits.org/). One-line messages are fine for small changes, but bigger changes should look like this:

    $ git commit -m "feat: a brief summary of the feature
    > 
    > A paragraph describing what changed and its impact."

## Coding conventions

Start reading our code and you'll get the hang of it. We optimize for readability (if we dont optimize for performance):

  * We use an .editorconfig file to enforce some basic style rules. Please install the [EditorConfig plugin](http://editorconfig.org/) for your editor.
  * This is open source software. Consider the people who will read your code, and make it look nice for them. It's sort of like driving a car: Perhaps you love doing donuts when you're alone, but with passengers the goal is to make the ride as smooth as possible.
  * Use modern C# features where appropriate, backwards compatibility is not a concern. We target .NET 10+.

Thanks,

Team twobrains
