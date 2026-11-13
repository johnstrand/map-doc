This application is for documenting data transformations. We will use blazor and .NET 10 for it.

It should look something like the BizTalk mapper, and allow the user to define types.

A type has these properties:

- Name
- Description
- Properties

A property has these properties:

- Name
- Description
- Type (string, boolean, number, array, object)
- Subtype (depends on type)
  - String: UUID, date, time, datetime, etc.
  - Boolean: n/a
  - Number: integer and float types
  - Array: Another type, which may include a subtype of its own. So an array could be string (uuid)
  - Object: Another type, including other types as defined in the application. May be self-referential
- Nullable
- Optional

A mapping consists of two base type, a source type and a target type. The mapping itself involves connecting properties to each other. The mapping tool should support
a preset of functoids (like ToString) and the ability to define custom functiods. The functoids should have 0..N inputs, and 1 output. When used in a transformation,
it should be possible to add a comment to the functoid. It should also be possible to add a comment to the mapping between two properties.

Mapping should be done by dragging a line between properties in the source and target types. We should validate that the properties are the same type, and warn if they're
not the same type, but both are scalar, and give an error if the user attempt to map an object/array to/from a scalar, or an object to/from an array.

For development purposes, we'll use Sqlite, but in deployed mode we'll use SQL Server in Azure.
