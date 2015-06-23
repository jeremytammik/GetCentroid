#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
#endregion

namespace GetCentroid
{
  [Transaction( TransactionMode.ReadOnly )]
  public class Command : IExternalCommand
  {
    const string _caption = "Get Centroid";

    class CentroidVolume // : Tuple<XYZ, double>
    {
      public CentroidVolume() // : base( XYZ.Zero, 0.0 )
      {
        Centroid = XYZ.Zero;
        Volume = 0.0;
      }

      public XYZ Centroid { get; set; }
      public double Volume { get; set; }

      override public string ToString()
      {
        return RealString( Volume ) + "@"
          + PointString( Centroid );
      }
    }

    /// <summary>
    /// Return a string for a real number
    /// formatted to two decimal places.
    /// </summary>
    public static string RealString( double a )
    {
      return a.ToString( "0.##" );
    }

    /// <summary>
    /// Return a string for an XYZ point
    /// or vector with its coordinates
    /// formatted to two decimal places.
    /// </summary>
    public static string PointString( XYZ p )
    {
      return string.Format( "({0},{1},{2})",
        RealString( p.X ),
        RealString( p.Y ),
        RealString( p.Z ) );
    }

    /// <summary>
    /// Return a string describing the given element:
    /// .NET type name,
    /// category name,
    /// family and symbol name for a family instance,
    /// element id and element name.
    /// </summary>
    public static string ElementDescription(
      Element e )
    {
      if( null == e )
      {
        return "<null>";
      }

      // For a wall, the element name equals the
      // wall type name, which is equivalent to the
      // family name ...

      FamilyInstance fi = e as FamilyInstance;

      string typeName = e.GetType().Name;

      string categoryName = ( null == e.Category )
        ? string.Empty
        : e.Category.Name + " ";

      string familyName = ( null == fi )
        ? string.Empty
        : fi.Symbol.Family.Name + " ";

      string symbolName = ( null == fi
        || e.Name.Equals( fi.Symbol.Name ) )
          ? string.Empty
          : fi.Symbol.Name + " ";

      return string.Format( "{0} {1}{2}{3}<{4} {5}>",
        typeName, categoryName, familyName,
        symbolName, e.Id.IntegerValue, e.Name );
    }

    /*
    void f()
    {
      var cx, cy, cz, volume, v, i, x1, y1, z1, x2, y2, z2, x3, y3, z3;
      volume = 0;
      cx = 0; cy = 0; cz = 0;
      // Assuming vertices are in vertX[i], vertY[i], and vertZ[i]
      // and faces are faces[i, j] where the first index indicates the 
      // face and the second index indicates the vertex of that face
      // The value in the faces array is an index into the vertex array
      i = 0;
      repeat (numFaces) {
        x1 = vertX[faces[i, 0]]; y1 = vertY[faces[i, 0]]; z1 = vertZ[faces[i, 0]];
        x2 = vertX[faces[i, 1]]; y2 = vertY[faces[i, 1]]; z2 = vertZ[faces[i, 1]];
        x3 = vertX[faces[i, 2]]; y3 = vertY[faces[i, 2]]; z3 = vertZ[faces[i, 2]];
        v = x1*(y2*z3 - y3*z2) + y1*(z2*x3 - z3*x2) + z1*(x2*y3 - x3*y2);
        volume += v;
        cx += (x1 + x2 + x3)*v;
        cy += (y1 + y2 + y3)*v;
        cz += (z1 + z2 + z3)*v;
        i += 1;
      }
      // Set centroid coordinates to their final value
      cx /= 4 * volume;
      cy /= 4 * volume;
      cz /= 4 * volume;
      // And, just in case you want to know the total volume of the model:
      volume /= 6;
    }
    */

    CentroidVolume GetCentroid( Solid solid )
    {
      CentroidVolume cv = new CentroidVolume();
      double v;
      XYZ v0, v1, v2;
      
      SolidOrShellTessellationControls controls 
        = new SolidOrShellTessellationControls();

      controls.LevelOfDetail = 0;
      
      TriangulatedSolidOrShell triangulation = null;

      try
      {
        triangulation 
          = SolidUtils.TessellateSolidOrShell( 
            solid, controls );
      }
      catch( Autodesk.Revit.Exceptions
        .InvalidOperationException )
      {
        return null;
      }

      int n = triangulation.ShellComponentCount;
      
      for( int i = 0; i < n; ++i )
      {
        TriangulatedShellComponent component 
          = triangulation.GetShellComponent( i );

        int m = component.TriangleCount;

        for( int j = 0; j < m; ++j )
        {
          TriangleInShellComponent t 
            = component.GetTriangle( j );

          v0 = component.GetVertex( t.VertexIndex0 );
          v1 = component.GetVertex( t.VertexIndex1 );
          v2 = component.GetVertex( t.VertexIndex2 );

          v = v0.X*(v1.Y*v2.Z - v2.Y*v1.Z) 
            + v0.Y*(v1.Z*v2.X - v2.Z*v1.X) 
            + v0.Z*(v1.X*v2.Y - v2.X*v1.Y);

          cv.Centroid += v * (v0 + v1 + v2);
          cv.Volume += v;
        }
      }

      // Set centroid coordinates to their final value

      cv.Centroid /= 4 * cv.Volume;

      XYZ diffCentroid = cv.Centroid 
        - solid.ComputeCentroid();

      Debug.Assert( 0.6 > diffCentroid.GetLength(),
        "expected centroid approximation to be "
        + "similar to solid ComputeCentroid result" );
     
      // And, just in case you want to know 
      // the total volume of the model:

      cv.Volume /= 6;

      double diffVolume = cv.Volume - solid.Volume;

      Debug.Assert( 0.3 > Math.Abs( 
        diffVolume / cv.Volume ),
        "expected volume approximation to be "
        + "similar to solid Volume property value" );

      return cv;
    }

    /// <summary>
    /// Calculate centroid for all non-empty solids 
    /// found for the given element. Family instances 
    /// may have their own non-empty solids, in which 
    /// case those are used, otherwise the symbol geometry.
    /// The symbol geometry could keep track of the 
    /// instance transform to map it to the actual 
    /// project location. Instead, we ask for 
    /// transformed geometry to be returned, so the 
    /// resulting solids are already in place.
    /// </summary>
    CentroidVolume GetCentroid(
      Element e,
      Options opt )
    {
      CentroidVolume cv = null;

      GeometryElement geo = e.get_Geometry( opt );

      Solid s;

      if( null != geo )
      {
        // List of pairs of centroid, volume for each solid

        List<CentroidVolume> a 
          = new List<CentroidVolume>();

        Document doc = e.Document;

        if( e is FamilyInstance )
        {
          geo = geo.GetTransformed(
            Transform.Identity );
        }

        GeometryInstance inst = null;

        CentroidVolume cv1;

        foreach( GeometryObject obj in geo )
        {
          s = obj as Solid;

          if( null != s
            && 0 < s.Faces.Size
            && SolidUtils.IsValidForTessellation( s )
            && (null != ( cv1 = GetCentroid( s ) ) ) )
          {
            a.Add( cv1 );
          }
          inst = obj as GeometryInstance;
        }

        if( 0 == a.Count && null != inst )
        {
          geo = inst.GetSymbolGeometry();

          foreach( GeometryObject obj in geo )
          {
            s = obj as Solid;

            if( null != s
              && 0 < s.Faces.Size
              && SolidUtils.IsValidForTessellation( s )
              && (null != ( cv1 = GetCentroid( s ) ) ) )
            {
              a.Add( cv1 );
            }
          }
        }

        // Get the total centroid from the partial
        // contributions. Each contribution is weighted
        // with its associated volume, which needs to 
        // be factored out again at the end.

        if( 0 < a.Count )
        {
          cv = new CentroidVolume();
          foreach( CentroidVolume cv2 in a )
          {
            cv.Centroid += cv2.Volume * cv2.Centroid;
            cv.Volume += cv2.Volume;
          }
          cv.Centroid /= a.Count * cv.Volume;
        }
      }
      return cv;
    }

    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      UIApplication uiapp = commandData.Application;
      UIDocument uidoc = uiapp.ActiveUIDocument;
      Application app = uiapp.Application;
      Document doc = uidoc.Document;
      List<ElementId> ids = new List<ElementId>();
      Selection sel = uidoc.Selection;
      //SelElementSet set = sel.Elements; // 2014
      ICollection<ElementId> selids = sel.GetElementIds(); // 2015

      //if( 0 < set.Size ) // 2014
      if( 0 < selids.Count ) // 2015
      {
        //foreach( Element e in set ) // 2014
        //{
        //  ids.Add( e.Id ); // 2014
        //}

        ids.AddRange( selids ); // 2015
      }
      else
      {
        if( ViewType.Internal == doc.ActiveView.ViewType )
        {
          TaskDialog.Show( _caption,
            "Cannot pick elements in this view: "
            + doc.ActiveView.Name );

          return Result.Failed;
        }

        try
        {
          IList<Reference> refs = sel.PickObjects(
            ObjectType.Element,
            "Please select some elements" );

          foreach( Reference r in refs )
          {
            ids.Add( r.ElementId );
          }
        }
        catch( Autodesk.Revit.Exceptions.OperationCanceledException )
        {
          return Result.Cancelled;
        }
      }

      Options opt = app.Create.NewGeometryOptions();

      foreach( ElementId id in ids )
      {
        Element e = doc.GetElement( id );

        CentroidVolume cv = GetCentroid( e, opt );

        Debug.Print( "{0} {1}", 
          (null == cv ? "<nil>" : cv.ToString()),
          ElementDescription( e ) );
      }
      return Result.Succeeded;
    }
  }
}
