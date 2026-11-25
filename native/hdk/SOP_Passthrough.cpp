#include <UT/UT_DSOVersion.h>
#include <UT/UT_HDKVersion.h>
#include <OP/OP_Operator.h>
#include <OP/OP_OperatorTable.h>
#include <OP/OP_AutoLockInputs.h>
#include <SOP/SOP_Node.h>
#include <PRM/PRM_Template.h>

static_assert(HDK_API_VERSION == 22000475, "Unqualified HDK API");

namespace {
class Passthrough final : public SOP_Node {
public:
    Passthrough(OP_Network* net, const char* name, OP_Operator* op)
        : SOP_Node(net, name, op) {}
    static OP_Node* create(OP_Network* net, const char* name, OP_Operator* op) {
        return new Passthrough(net, name, op);
    }
protected:
    OP_ERROR cookMySop(OP_Context& context) override {
        OP_AutoLockInputs lock(this);
        if (lock.lock(context) >= UT_ERROR_ABORT) return error();
        duplicateSource(0, context);
        return error();
    }
};
PRM_Template parameters[] = { PRM_Template() };
}

void newSopOperator(OP_OperatorTable* table) {
    table->addOperator(new OP_Operator("dxsharp_passthrough", "DXSharp Passthrough",
        Passthrough::create, parameters, 1, 1));
}
