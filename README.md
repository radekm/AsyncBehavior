# Behavior of async primitives in .NET 7

This project examines behavior of `Channel`s and `Task`s in .NET 7.

I believe it's very important to know how these primitives behave
when they return a value and when they raise an exception and which exception.
Otherwise it's very hard to write correct code.
So we made a few experiments in F# which demonstrate the behavior.

# About license

The license of this project looks like the 3-clause BSD License
but it has additional conditions:

- This software must not be used for military purposes.
- Source code must not be used for training or validating AI models.
  For example AI models which generate source code.
