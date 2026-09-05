#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UniversalWorkflowFramework.Reference.Models;

namespace UniversalWorkflowFramework.EventDriven.Models
{
    public class ReferenceChangeEventArgs : EventArgs

    {
        public Core.Models.ResourceIdentifier Resource { get; }
        public Reference.Models.ReferenceChangeType ChangeType { get; }
        public Dictionary<string, object>? OldMetadata { get; }
        public Dictionary<string, object>? NewMetadata { get; }
        public DateTime Timestamp { get; }
        public string? RelationshipType { get; }
        public Core.Models.ResourceIdentifier? RelatedResource { get; }

        public ReferenceChangeEventArgs(
            Core.Models.ResourceIdentifier resource,
            Reference.Models.ReferenceChangeType changeType,
            Dictionary<string, object>? oldMetadata = null,
            Dictionary<string, object>? newMetadata = null,
            string? relationshipType = null,
            Core.Models.ResourceIdentifier? relatedResource = null)
        {
            Resource = resource ?? throw new ArgumentNullException(nameof(resource));
            ChangeType = changeType;
            OldMetadata = oldMetadata;
            NewMetadata = newMetadata;
            RelationshipType = relationshipType;
            RelatedResource = relatedResource;
            Timestamp = DateTime.UtcNow;
        }
    }

    public class ReferenceRelationshipChangedEventArgs : EventArgs
    {
        public Core.Models.ResourceIdentifier Source { get; }
        public Core.Models.ResourceIdentifier Target { get; }
        public string RelationshipType { get; }
        public bool IsAdded { get; }
        public DateTime Timestamp { get; }

        public ReferenceRelationshipChangedEventArgs(
            Core.Models.ResourceIdentifier source,
            Core.Models.ResourceIdentifier target,
            string relationshipType,
            bool isAdded)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            RelationshipType = relationshipType;
            IsAdded = isAdded;
            Timestamp = DateTime.UtcNow;
        }
    }

    public class BulkReferenceChangeEventArgs : EventArgs
    {
        public ReadOnlyCollection<ReferenceChangeEventArgs> Changes { get; }
        public DateTime Timestamp { get; }

        public BulkReferenceChangeEventArgs(IEnumerable<ReferenceChangeEventArgs> changes)
        {
            Changes = changes.ToList().AsReadOnly();
            Timestamp = DateTime.UtcNow;
        }
    }
}