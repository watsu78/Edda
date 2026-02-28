using System;

namespace Edda.Classes.MapEditorNS.EditManager {
public class Edit<T> : ICloneable {
    public bool isAdd;
    public bool isMove;
    public T item;
    public Edit(bool isAdd, T item, bool isMove = false) {
        this.isAdd = isAdd;
        this.item = item;
        this.isMove = isMove;
    }
    public object Clone() {
        return new Edit<T>(isAdd, item, isMove);
    }
}
}